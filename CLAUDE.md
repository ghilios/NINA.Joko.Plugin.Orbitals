# Project layout

This solution has two C# projects.

| Project | Type | What it is |
|---|---|---|
| `NINA.Joko.Plugin.Orbitals/` | WPF class library | The plugin itself. Hosted by NINA. |
| `TestApp/` | WPF executable | A standalone harness app that exercises the plugin against a real mount/ASCOM. Not a test project. |

When the user says:

- **"TestApp"** → they mean `TestApp/TestApp.csproj`. The standalone WPF exe.
- **"the plugin"** → `NINA.Joko.Plugin.Orbitals/`.

There is no unit-test project yet. `.github/workflows/tests.yml` expects one at `NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` with a `coverlet.runsettings` alongside it — the workflow will fail until that project is added.

When upgrading NINA.Plugin or any package the plugin csproj brings in transitively, both `TestApp/TestApp.csproj` and the plugin csproj usually need bumping together — `TestApp` has its own direct `<PackageReference Include="NINA.Plugin" ... />` which NuGet treats as a separate constraint.

# Bumping the minimum supported NINA version

The same version string lives in three places. All three must move together or the release workflow publishes the manifest under the wrong path:

- `NINA.Joko.Plugin.Orbitals/Properties/AssemblyInfo.cs` — `AssemblyMetadata("MinimumApplicationVersion", "...")`
- `NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj` and `TestApp/TestApp.csproj` — `<PackageReference Include="NINA.Plugin" Version="..." />`
- `.github/workflows/build-and-release.yml` — `PLUGIN_MANIFEST_PATH` (its third segment is the min-app version; it directs the manifest into the right subdirectory of `nina.plugin.manifests`)
