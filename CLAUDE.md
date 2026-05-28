# Branch context — this is the NINA 3.2 release line

This branch targets **NINA 3.2.0.9001** and **.NET 8** (`net8.0-windows7.0`). It exists alongside `develop`, which targets **NINA 3.3.0.1003-nightly** and **.NET 10**. Most ongoing feature work lands on `develop`; this branch holds the same fixes, tests, and CI but pinned to the 3.2-compatible toolchain.

## PR target branch

All PRs opened from this branch — or from any `backport/develop-to-3.2*` branch — **must target `release/3.2`**, not `develop`. `develop` is the NINA 3.3 line and a separate release. When using `gh pr create`, always pass `--base release/3.2`. If a PR is opened against the wrong base, re-target it before merging.

## Porting from develop

When `develop` has new commits that need to flow back here, use the `port-from-develop` skill at `.claude/skills/port-from-develop/SKILL.md`. It tracks the last-ported develop commit in `.claude/skills/port-from-develop/last-ported-commit` so each run only re-merges what's new. The skill also enumerates the canonical 3.2 pin-back checklist (NINA.Plugin version, TFM, SQLite FTS5 loader, workflow paths) and explicitly forbids changing `AssemblyVersion` / `AssemblyFileVersion` during the merge — those belong to the 3.2 release line and only move under deliberate release commits here.

A few concrete differences from `develop` you must preserve when editing here:

- `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` and `TestApp/TestApp.csproj` — `<PackageReference Include="NINA.Plugin" Version="3.2.0.9001" />`. Do **not** bump to 3.3.x on this branch.
- `TargetFramework` is `net8.0-windows7.0` in all three csprojs (plugin, TestApp, tests). NINA 3.2 runs on .NET 8.
- `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` — `AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")`.
- `NINA.Joko.Plugin.Orbitals/Utility/TrigramStringMap.cs` — the `connection.EnableExtensions(true)` / `connection.LoadExtension("SQLite.Interop.dll", "sqlite3_fts5_init")` calls are **uncommented** here. NINA 3.2 ships a SQLite build without FTS5 loaded, so the plugin must load it. NINA 3.3 includes FTS5 itself, which is why `develop` has those lines commented out.
- `.github/workflows/build-and-release.yml` — `PLUGIN_MANIFEST_PATH: "o/Orbitals/3.2.0"` (third segment is NINA major.minor and determines where the manifest is published in `nina.plugin.manifests`).
- `.github/workflows/tests.yml` — triggers **only on `pull_request` into `release/3.2`** (no `push:` trigger), uses `dotnet-version: '8.0.x'`. The `push:` trigger was removed deliberately to avoid double runs (a push to a `backport/develop-to-3.2*` branch + its PR both firing); PRs from backport branches are still covered because their base is `release/3.2`.

# Project layout

This solution has three C# projects.

| Project | Type | What it is |
|---|---|---|
| `NINA.Joko.Plugin.Orbitals/` | WPF class library | The plugin itself. Hosted by NINA. |
| `NINA.Joko.Plugin.Orbitals.Tests/` | NUnit test project | Unit tests for the plugin. Built and run by `.github/workflows/tests.yml`. |
| `TestApp/` | WPF executable | A standalone harness app that exercises the plugin against a real mount/ASCOM. Not a test project. |

When the user says:

- **"TestApp"** → they mean `TestApp/TestApp.csproj`. The standalone WPF exe.
- **"the plugin"** → `NINA.Joko.Plugin.Orbitals/`.
- **"the tests" / "the test project"** → `NINA.Joko.Plugin.Orbitals.Tests/`.

When upgrading any package the plugin csproj brings in transitively, both `TestApp/TestApp.csproj` and the plugin csproj usually need bumping together — `TestApp` has its own direct `<PackageReference Include="NINA.Plugin" ... />` which NuGet treats as a separate constraint. The test project does not pin `NINA.Plugin` directly (it picks up the plugin transitively via `<ProjectReference>`), so it does not need its own bump.

# Bumping the minimum supported NINA version

> On this branch the minimum supported NINA version is fixed at **3.2.0.9001** as part of holding the 3.2 release line. Don't bump it here — version-line changes belong on `develop`.

The same version string lives in these places. All must move together or the release workflow publishes the manifest under the wrong path:

- `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` — `AssemblyMetadata("MinimumApplicationVersion", "...")`
- `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` and `TestApp/TestApp.csproj` — `<PackageReference Include="NINA.Plugin" Version="..." />`
- `.github/workflows/build-and-release.yml` — `PLUGIN_MANIFEST_PATH` (its third segment is the min-app version; it directs the manifest into the right subdirectory of `nina.plugin.manifests`)
