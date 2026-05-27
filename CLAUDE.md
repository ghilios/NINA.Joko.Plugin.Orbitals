# Plan files

When plan mode produces a plan file, save it inside this repo's `plans/` directory (e.g. `plans/<short-feature-name>.md`). Do not leave it under `~/.claude/plans/` — those are user-global and aren't checked in. If the plan-mode harness writes to `~/.claude/plans/` first, copy it over to `plans/` and commit it with the implementation.

# Project layout

This solution has three C# projects.

| Project | Type | What it is |
|---|---|---|
| `NINA.Joko.Plugin.Orbitals/` | WPF class library | The plugin itself. Hosted by NINA. |
| `TestApp/` | WPF executable | A standalone harness app that exercises the plugin against a real mount/ASCOM. Not a test project. |
| `NINA.Joko.Plugin.Orbitals.Tests/` | NUnit test project | Unit tests for the plugin. Uses NUnit 4 + Moq + FluentAssertions, with coverlet for coverage (`coverlet.runsettings` alongside the csproj). `.github/workflows/tests.yml` runs this on CI. |

When the user says:

- **"TestApp"** → they mean `TestApp/TestApp.csproj`. The standalone WPF exe.
- **"the plugin"** → `NINA.Joko.Plugin.Orbitals/`.
- **"the tests"** → `NINA.Joko.Plugin.Orbitals.Tests/`. Run with `dotnet test` from the repo root.

Whenever you change plugin code, build the plugin **and** run the test project; the test project references the plugin, so a constructor change will surface there even if it doesn't break TestApp.

When upgrading NINA.Plugin or any package the plugin csproj brings in transitively, both `TestApp/TestApp.csproj` and the plugin csproj usually need bumping together — `TestApp` has its own direct `<PackageReference Include="NINA.Plugin" ... />` which NuGet treats as a separate constraint.

# Bumping the minimum supported NINA version

The same version string lives in three places. All three must move together or the release workflow publishes the manifest under the wrong path:

- `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` — `AssemblyMetadata("MinimumApplicationVersion", "...")`
- `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` and `TestApp/TestApp.csproj` — `<PackageReference Include="NINA.Plugin" Version="..." />`
- `.github/workflows/build-and-release.yml` — `PLUGIN_MANIFEST_PATH` (its third segment is the min-app version; it directs the manifest into the right subdirectory of `nina.plugin.manifests`)
