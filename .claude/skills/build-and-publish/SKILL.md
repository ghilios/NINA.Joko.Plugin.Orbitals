---
name: build-and-publish
description: Use when the user wants to cut a release of the Orbitals plugin from the develop branch (NINA 3.3 line) — tags the current HEAD with release/v<AssemblyVersion>, pushes it to GitHub to trigger the Build and Release workflow, then watches the workflow until it finishes (or fails). Triggers on phrases like "publish", "cut a release", "tag and publish", "release this build".
---

# Build and Publish (develop / 3.3)

## Overview

The Build and Release workflow (`.github/workflows/build-and-release.yml`) runs **only on a tag push** matching `release/v[0-9]+.[0-9]+.[0-9]+.[0-9]+`. There is no manual `workflow_dispatch`. To publish, you create that tag locally pointed at the commit you want to ship, push it, and watch the workflow build the DLL, create the GitHub Release, and open a manifest PR against `isbeorn/nina.plugin.manifests`.

This skill automates everything from "I want to ship" to "the workflow finished" with one confirmation gate at the irreversible step (tag push).

## Source of truth: AssemblyInfo.cs

`NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` carries the canonical version:

```csharp
[assembly: AssemblyVersion("3.3.0.2")]
[assembly: AssemblyFileVersion("3.3.0.2")]
```

The csproj has `GenerateAssemblyInfo=false`, so the workflow does NOT inject a version from the tag — the DLL gets whatever AssemblyInfo says, and the tag is only used to name the GitHub Release and the artifact filenames. **Tag and AssemblyVersion must match.** If they don't, the published zip will be named after the tag but contain a DLL stamped with a different version, and NINA will refuse to install over an existing copy or will install a confusingly-misversioned plugin.

Bumping the version is a separate manual step (edit AssemblyInfo, commit, push) — this skill assumes it's already done.

## When NOT to use

- **Working tree dirty, or HEAD not pushed.** Tagging an unpushed commit means the tag refers to a SHA only on your machine; pushing the tag will succeed and the workflow will build, but the release will point at a commit no one else has on a branch. Push the branch first.
- **Tag already exists.** Means that version was already published (or attempted). Bump AssemblyVersion and re-run.
- **AssemblyVersion is on the 3.2 line.** The 3.2.x.y series ships from `release/3.2`, not from develop. Switch branches before running this.

### CRITICAL: workflow tag regex is unscoped on this branch

Unlike `release/3.2`, the tag filter on develop is **not** version-scoped:

```yaml
- 'release/v[0-9]+.[0-9]+.[0-9]+.[0-9]+'
```

That means a stray `release/v3.2.x.y` tag pushed here would still trigger the workflow — but it would publish into `PLUGIN_MANIFEST_PATH: o/Orbitals/3.3.0`, putting a 3.2-versioned manifest into the 3.3 directory. The workflow won't catch this. **This skill must.** Step 2 below asserts the AssemblyVersion is on the 3.3 line before going any further.

## Procedure

### 1. Preflight — gather state

```bash
# Branch + clean tree
git rev-parse --abbrev-ref HEAD          # must be develop
git status --porcelain                   # must be empty

# HEAD must be pushed
git fetch origin
git rev-parse HEAD                       # local HEAD
git rev-parse origin/develop             # must equal local HEAD
```

If any check fails, stop and surface it to the user. Don't auto-push the branch — pushing branch HEAD is a separate decision the user should make explicitly.

### 2. Read AssemblyVersion and guard the major version

```bash
grep -oP 'AssemblyVersion\("\K[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+' \
  NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs
```

**Assert the version starts with `3.3.`** This is the only guard against publishing a wrong-line manifest into `o/Orbitals/3.3.0` (see CRITICAL note above). If the version doesn't start with `3.3.`, stop — either you're on the wrong branch, or AssemblyInfo wasn't bumped for the 3.3 release.

Compose the tag:

```bash
VERSION=3.3.0.2                          # from grep above
TAG=release/v${VERSION}
```

### 3. Verify tag doesn't already exist

```bash
git rev-parse --verify "refs/tags/${TAG}" 2>/dev/null   # must fail
git ls-remote --tags origin "${TAG}"                    # must be empty
```

If either finds the tag, stop. The user needs to bump AssemblyVersion (and commit + push) before re-running.

### 4. Confirm with the user

Tag push is the irreversible step — it triggers a public GitHub Release and a PR into the upstream manifests repo. Show the user:

- tag name (`release/v3.3.0.2`)
- HEAD SHA + commit subject (`git log -1 --oneline`)
- assembly version
- "Push tag and start the Build and Release workflow?"

Wait for explicit confirmation. Don't proceed on silence or implicit approval (auto-mode doesn't extend to pushing release tags).

### 5. Create and push the tag

Annotated tag (not lightweight — gives the release a real tagger + message):

```bash
git tag -a "${TAG}" -m "Release ${VERSION}"
git push origin "${TAG}"
```

### 6. Monitor the workflow

The workflow takes ~30s to register after the tag push. Poll briefly to find the run, then watch it.

```bash
# Find the run for this tag. headBranch == tag name for tag-pushed workflows.
# Retry up to ~6 times (60s) — GitHub can take a moment to schedule the run.
for i in 1 2 3 4 5 6; do
  RUN_ID=$(gh run list --workflow="Build and Release" --event=push --limit=10 \
    --json databaseId,headBranch \
    --jq ".[] | select(.headBranch==\"${TAG}\") | .databaseId" | head -1)
  [ -n "$RUN_ID" ] && break
  sleep 10
done
[ -z "$RUN_ID" ] && { echo "No run found for ${TAG} after 60s"; exit 1; }

# Stream until done, then exit nonzero on failure.
gh run watch "$RUN_ID" --exit-status
```

`gh run watch --exit-status` blocks until the run finishes and exits nonzero if any job failed. Set the Bash `timeout` parameter on this call to `1800000` (30 min) — the Windows runner build is slow.

### 7. Report outcome

On success, surface the artifacts the user actually wants links to:

```bash
# GitHub Release page
echo "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/tag/${TAG}"

# Manifest PR (the publish-manifest job opens one against isbeorn/nina.plugin.manifests)
gh pr list --repo isbeorn/nina.plugin.manifests --author ghilios --state open --limit 3
```

The `publish-manifest` job opens a PR titled `Add manifest for Orbitals <VERSION>` against `isbeorn/nina.plugin.manifests`. That PR is what actually makes the release visible inside NINA's plugin browser — the GitHub Release alone isn't enough. Tell the user the manifest PR still needs upstream review/merge.

On failure, `gh run view "$RUN_ID" --log-failed | tail -200` is usually enough to diagnose. Common failures are in the next section.

## Common failures

| Symptom | Cause | Fix |
|---|---|---|
| Workflow never starts after push | Tag doesn't match regex (e.g. `release/v3.3.0` — only 3 segments) | Delete tag (`git tag -d`, `git push origin :refs/tags/<tag>`), use 4-segment version |
| Manifest landed in `o/Orbitals/3.3.0` but DLL is a 3.2 build | Tagged a 3.2 AssemblyVersion on develop (skill's step-2 guard was bypassed) | Delete the GitHub Release + tag, fix AssemblyInfo, re-tag. Close the wrong manifest PR upstream. |
| `build-and-release` job fails at `dotnet build` | Plugin csproj changed but `TestApp.csproj` didn't get the matching NINA.Plugin bump | See CLAUDE.md "Bumping the minimum supported NINA version" — both csprojs and the workflow's `PLUGIN_MANIFEST_PATH` must move together |
| `publish-manifest` skipped | `check-repo-exists` returned `exists=false` — ghilios/nina.plugin.manifests fork is missing | Fork `isbeorn/nina.plugin.manifests` to your account, re-run the failed job from the Actions tab |
| `publish-manifest` fails at `Clone manifest repo` with `Authentication failed` | `PAT` secret expired or lacks `repo` scope | Renew the `PAT` repo secret with `repo` scope, then `gh run rerun <run-id> --failed` |
| `publish-manifest` fails at `Create pull request` with `missing required scope 'read:org'` | `PAT` secret has `repo` but not `read:org` (gh auth login requires it) | Renew `PAT` with BOTH `repo` and `read:org` scopes, then `gh run rerun <run-id> --failed` |
| Release zip exists but plugin won't install in NINA | Tag version ≠ AssemblyVersion | Delete the GitHub Release + tag, fix AssemblyInfo, re-tag |

## Rollback

If a bad release goes out, **delete both the GitHub Release and the tag** so future tag pushes for the same version aren't no-ops:

```bash
gh release delete "${TAG}" --yes --cleanup-tag
git fetch --prune origin '+refs/tags/*:refs/tags/*'   # sync local
```

`--cleanup-tag` removes the remote tag in the same call. If the manifest PR was already opened, close it manually on `isbeorn/nina.plugin.manifests`.

## Quick reference

| Step | Command |
|---|---|
| Read version | `grep -oP 'AssemblyVersion\("\K[0-9.]+' NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` |
| Tag | `git tag -a release/v$VERSION -m "Release $VERSION"` |
| Push | `git push origin release/v$VERSION` |
| Find run | `gh run list --workflow="Build and Release" --event=push --limit=10 --json databaseId,headBranch --jq '.[] | select(.headBranch=="release/v'$VERSION'") | .databaseId'` |
| Watch | `gh run watch $RUN_ID --exit-status` (Bash `timeout: 1800000`) |
| Inspect failure | `gh run view $RUN_ID --log-failed \| tail -200` |
| Re-run failed jobs | `gh run rerun $RUN_ID --failed` |
| Delete bad release | `gh release delete release/v$VERSION --yes --cleanup-tag` |
