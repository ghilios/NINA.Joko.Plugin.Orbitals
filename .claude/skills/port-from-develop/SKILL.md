---
name: port-from-develop
description: Use when bringing new develop commits into release/3.2 (the NINA 3.2 release line) in this repo. Triggers any time develop has moved ahead of last-ported-commit and those changes need to flow back to the 3.2 branch.
---

# Port from develop to release/3.2

## Overview

`release/3.2` holds the NINA 3.2 release line; `develop` targets NINA 3.3. Most fixes and tests land on `develop` first and need to be backported here. This skill runs that backport against a tracked baseline so each run only re-merges what's new.

## State file

`.claude/skills/port-from-develop/last-ported-commit` — single line, full 40-char SHA of the most recent develop commit already merged here. Read it at start. Overwrite it at the end of a successful port.

## Procedure

1. **Sync.** `git fetch origin`, then `git checkout release/3.2 && git pull --ff-only`.
2. **Read baseline.** `LAST=$(cat .claude/skills/port-from-develop/last-ported-commit)`.
3. **Check what's new.** `git log --oneline $LAST..origin/develop`. If empty, stop — nothing to port.
4. **Create branch.** `NEW=$(git rev-parse origin/develop); git checkout -b backport/develop-to-3.2-$(git rev-parse --short=7 $NEW)`.
5. **Merge.** `git merge --no-ff --no-commit origin/develop`.
6. **Resolve conflicts.** Conflicts almost always show up in `AssemblyInfo.cs` (versions) and any file touched by both branches. Rules:
   - **NEVER modify `AssemblyVersion` or `AssemblyFileVersion`** — they belong to the 3.2 line and are bumped only by deliberate release commits on this branch. Always pick HEAD's value.
   - `MinimumApplicationVersion` stays at `"3.2.0.9001"`. Always pick HEAD's value.
   - For code conflicts, prefer HEAD when both branches solved the same bug differently (e.g. `OrbitalElementsObject.Clone()` on release/3.2 vs inline clone on develop). Otherwise prefer develop's content.
7. **Re-pin 3.2 toolchain.** After conflict resolution, before committing the merge, apply the canonical pins (verify each — only edit if develop has drifted it). Pin checklist in next section.
8. **Commit merge.** `git commit -m "Merge develop into backport/develop-to-3.2-<sha>"`.
9. **Build + test.**
   ```
   rtk dotnet restore NINA.Joko.Plugin.Orbitals.sln
   rtk dotnet build   NINA.Joko.Plugin.Orbitals.sln -c Debug --nologo --no-restore
   rtk dotnet test    NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj -c Debug --nologo --no-restore --no-build
   ```
   rtk's `fail` header on green builds is cosmetic — trust `0 errors` and the test count. If errors, fix on this branch.
10. **Update state.** `echo $NEW > .claude/skills/port-from-develop/last-ported-commit`.
11. **Commit pins + state.** Stage the pin-back changes and the state file. Commit: `Port develop $(LAST_SHORT)..$(NEW_SHORT) — pin to NINA 3.2.0.9001 / .NET 8`.
12. **Push + PR.** Push the branch. Open a PR with **base = `release/3.2`** (never `develop`). See CLAUDE.md "PR target branch" section.

## Release workflow trigger model (read before editing build-and-release.yml)

`build-and-release.yml` triggers on tag push only:

```yaml
on:
  push:
    tags:
      - 'release/v[0-9]+.[0-9]+.[0-9]+.[0-9]+'
```

Tag triggers in GitHub Actions ignore branch — any tag matching the pattern fires the workflow at **the tag's commit**, and the workflow file at that commit is what executes. So:

- A `release/v3.1.0.x` tag created on a commit in the `release/3.2` lineage uses that commit's `build-and-release.yml`, which has `PLUGIN_MANIFEST_PATH: "o/Orbitals/3.2.0"` and the `net8.0-windows7.0` build dir → publishes to the 3.2 manifest path.
- A `release/v3.3.x.x` tag on `develop`'s lineage uses develop's workflow, which has `o/Orbitals/3.3.0` and `net10.0-windows7.0` → publishes to the 3.3 manifest path.

**Consequence for ports:** the trigger itself never needs editing on this branch. What MUST hold after every port is that `release/3.2`'s copy of `build-and-release.yml` keeps the 3.2-line values (`PLUGIN_MANIFEST_PATH`, build dir). If develop has drifted those, the pin-back step has to put them back — otherwise the next `release/v3.1.0.x` tag publishes the manifest to the wrong directory. These are in the pin checklist below; treat the row about `build-and-release.yml` as load-bearing.

## 3.2 pin checklist

These values must hold after the merge. Audit each one; only edit if develop has drifted it.

| File | Setting | Required value |
|---|---|---|
| `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` | `<TargetFramework>` | `net8.0-windows7.0` |
| `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` | `<PackageReference Include="NINA.Plugin">` | `3.2.0.9001` |
| `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` | `SQLite.Interop.dll` / `System.Data.SQLite` refs | **removed** (transitive on 3.2) |
| `NINA.Joko.Plugin.Orbitals/Utility/TrigramStringMap.cs` | `EnableExtensions` / `LoadExtension("SQLite.Interop.dll", "sqlite3_fts5_init")` | **uncommented** |
| `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` | `MinimumApplicationVersion` | `3.2.0.9001` |
| `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` | `AssemblyVersion` / `AssemblyFileVersion` | **untouched** — keep HEAD |
| `NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` | `<TargetFramework>` | `net8.0-windows7.0` |
| `TestApp/TestApp.csproj` | `<TargetFramework>` | `net8.0-windows7.0` |
| `TestApp/TestApp.csproj` | `<PackageReference Include="NINA.Plugin">` | `3.2.0.9001` |
| `TestApp/TestApp.csproj` | `System.ComponentModel.Composition` | `8.0.0` (present) |
| `.github/workflows/build-and-release.yml` | `on.push.tags` pattern | `'release/v[0-9]+.[0-9]+.[0-9]+.[0-9]+'` (unchanged — same pattern on both lines; the branch context comes from the tag's commit, not the trigger) |
| `.github/workflows/build-and-release.yml` | `PLUGIN_MANIFEST_PATH` | `"o/Orbitals/3.2.0"` |
| `.github/workflows/build-and-release.yml` | `Prepare package` build dir | `net8.0-windows7.0` |
| `.github/workflows/tests.yml` | trigger branches | `release/3.2`, `backport/develop-to-3.2*` |
| `.github/workflows/tests.yml` | `dotnet-version` | `'8.0.x'` |

## Red flags

| Symptom | What it means |
|---|---|
| `AssemblyVersion` or `AssemblyFileVersion` differs from `release/3.2`'s value | You took develop's side in the conflict. Revert it. |
| `MinimumApplicationVersion` shows `3.3.x` | Same — re-pin to `3.2.0.9001`. |
| Build error mentioning `SQLiteConnection` or `LoadExtension` | The `TrigramStringMap.cs` lines were left commented. Re-enable. |
| `bin\Release\net10.0-windows7.0` referenced anywhere | A `net10` slipped through. Search for `net10` across the diff. |
| PR shows `base: develop` | Wrong target. Edit PR to base on `release/3.2`. |

## Common mistakes

- **Editing the state file before the build passes.** Update it only after build + tests are green; otherwise a failed port leaves a misleading baseline.
- **Squash-merging the backport PR into release/3.2.** Use a merge commit so the develop commits stay reachable from release/3.2's history — that's what `last-ported-commit` relies on for diff range checks.
- **Forgetting to push the state file.** It must be committed on the same branch as the pin-back changes, or the next port can't compute the right range.
