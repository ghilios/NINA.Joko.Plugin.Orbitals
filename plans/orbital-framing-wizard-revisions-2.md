# Plan: Screen-space drag + canvas zoom + sky-survey background (wizard revisions, round 2)

## Context

Three new UX feedback items on the Orbital Framing Wizard, building on the work just merged via PR #18 + commit `aa003de`:

1. **Drag should track screen direction, not the rectangle's local frame.** When the rectangle is rotated and the user drags it up on screen, the rectangle currently slides along its own local +X/+Y axes (so a 45°-rotated rectangle moves diagonally for a vertical drag). The root cause is the `TransformGroup` order on the rectangle: `<TranslateTransform/><RotateTransform/>` — WPF composes those as `M = M_rotate * M_translate`, so translation is applied in the element's local frame *before* rotation. Swapping the order to `<RotateTransform/><TranslateTransform/>` makes translation post-rotation (parent/screen space), which is the desired behavior. No math change.

2. **Add zoom buttons + scrollbars.** The user wants explicit zoom-in / zoom-out / reset controls; when zoomed in, scrollbars appear so the rest of the canvas is reachable. No interactive zoom mechanism exists today (mouse-wheel is only consumed by the rectangle rotation handler).

3. **Image Source dropdown + immediate background load, default = offline sky map.** Today the canvas's `BackgroundImage` is hard-coded to `null` (per the explore: `OrbitalFramingWizardVM.cs:208`). The original plan called for this (§3, §5) but it was deferred. Now: inject `ISkySurveyFactory`, expose a `SelectedImageSource` (`SkySurveySource` enum) ComboBox defaulting to NINA's offline sky-atlas source, fetch the survey image immediately on `Initialize(target)` (the body's J2000 coordinates are already known pre-capture), re-fetch on source change and on capture (with the captured FOV). Mirror NINA's framing-assistant behavior: persist the user's last source pick to `ActiveProfile.FramingAssistantSettings.LastSelectedImageSource` so it round-trips with NINA's setting.

## Approach

Three files added to (none new); one constructor signature changes (so all VM-construction call sites are touched).

### 1. Drag in screen space — `View/OrbitalFramingCanvas.xaml`

One-line XAML fix: reorder the rectangle's `TransformGroup` so rotation composes before translation. Current (`OrbitalFramingCanvas.xaml:51-56`):

```xml
<TransformGroup>
    <TranslateTransform x:Name="RectTranslate" X="0" Y="0" />
    <RotateTransform x:Name="RectRotation" Angle="0" />
</TransformGroup>
```

After:

```xml
<TransformGroup>
    <RotateTransform x:Name="RectRotation" Angle="0" />
    <TranslateTransform x:Name="RectTranslate" X="0" Y="0" />
</TransformGroup>
```

No code-behind change — `_canvasPxPerImagePx` conversion and the screen-pixel drag delta in `FramingRectangle_MouseMove` are already correct under the new order. `_rectangleOffsetXPx/YPx` still semantically represent image-pixel deltas in the (un-rotated) image frame, which equals screen frame, which equals where the translate moves the (rotated) rectangle visually.

**Verification thought experiment.** Capture a 30°-rotated frame, drag the rectangle vertically by 100 canvas px. New behaviour: rectangle moves straight up by 100 canvas px. `RectangleOffsetYPx = -100 / scale` image px. `Coordinates.Shift(0, -100/scale, CapturedImageRotation, pixscale, pixscale)` returns a coord at +100/scale pixels north in the image's local frame — but since the image is displayed un-rotated, that *is* north on screen. Math + visual agree.

### 2. Zoom + scrollbars

Architecture: `Zoom` lives as a `double` DP on the canvas; the VM owns the integer-step state and exposes commands so the toolbar in the wizard view binds cleanly.

#### `OrbitalFramingCanvas.xaml(.cs)`

- New DP `Zoom` (double, default `1.0`, `FrameworkPropertyMetadataOptions.BindsTwoWayByDefault`). Callback writes through to a `ScaleTransform` set as `RootGrid.LayoutTransform` — using `LayoutTransform` (not `RenderTransform`) so the WPF layout system re-measures and the wrapping `ScrollViewer` can show scrollbars.
- Add a `ScaleTransform x:Name="RootScale"` on `RootGrid.LayoutTransform` initialized to 1.0.
- Optional ergonomics: handle `MouseWheel` with `Ctrl` held to zoom (1.1× per notch); skip if cursor is over the rectangle so it doesn't fight the rotation handler. If we go with the basic buttons-only path, leave this off the first pass.

#### `View/OrbitalFramingWizardView.xaml`

- Replace the existing canvas placement (currently a `<Grid>` inside `<Border Grid.Column="0">` at lines 49-50) with a `ScrollViewer` wrapping the same `<Grid>`. Set both scrollbar visibilities to `Auto`.
- Add a small overlay `StackPanel` (Horizontal, top-right) on top of the canvas with three buttons: "Zoom −", "Zoom +", "⤾ Reset". `Panel.ZIndex` ensures they sit above the canvas; `Background="#80000000"` keeps them legible over the survey image.
- Bind `Zoom="{Binding CanvasZoom}"` on the canvas.

#### `ViewModels/OrbitalFramingWizardVM.cs`

- Add `double CanvasZoom { get; private set; } = 1.0` with `RaisePropertyChanged()` on change.
- Three `IRelayCommand` properties: `ZoomInCommand`, `ZoomOutCommand`, `ResetZoomCommand`.
- Step constants: `ZoomStep = 1.25`, `MinZoom = 0.25`, `MaxZoom = 8.0`. Bound the result.
- Commands clamp into `[MinZoom, MaxZoom]`.

### 3. Image Source + immediate background load

#### `ViewModels/OrbitalFramingWizardVM.cs` — DI + state

- Add `ISkySurveyFactory skySurveyFactory` to the constructor (position after `IApplicationStatusMediator` for grouping; this is a breaking change to every test-call site — there are three setup paths in `OrbitalFramingWizardVMTests.cs` and one production call site in `OrbitalsVM.cs`).
- New properties:
  - `SkySurveySource SelectedImageSource` (initialized from `profileService.ActiveProfile.FramingAssistantSettings.LastSelectedImageSource`; if `default` or invalid, fall back to NINA's offline sky-atlas value — implementer to confirm the exact enum name, expected to be `SkySurveySource.SKYATLAS` or equivalent in `NINA.WPF.Base` / `NINA.Astrometry`).
  - `bool IsBackgroundLoading`.
  - `List<SkySurveySource> AvailableImageSources` (the full enum) for the ComboBox `ItemsSource`.
- Field: `CancellationTokenSource _backgroundLoadCts;` so source-change or coordinate-change can cancel an in-flight load.

#### `LoadBackgroundImageAsync(CancellationToken ct)` helper

```
1. Cancel any prior load (Cts.Cancel + Dispose).
2. Determine center coordinates:
   - Post-capture: CapturedImageCoordinates.
   - Pre-capture: selectedObject.PositionAt(DateTime.UtcNow).Coordinates.
3. Determine FOV (arcmin):
   - Post-capture: pixscale_arcsec × widthPx × BackgroundFovMultiplier / 60.
   - Pre-capture: from ActiveProfile.CameraSettings.PixelSize + TelescopeSettings.FocalLength,
     synthesized assumed sensor width (4000 px works for most ZWO/QHY APS-C),
     × BackgroundFovMultiplier; fallback 60 arcmin if either profile field is zero/missing.
4. IsBackgroundLoading = true; raise PC; await skySurveyFactory.Create(SelectedImageSource)
   .GetImage(name, coords, fov_arcmin, defaultPxW, defaultPxH, ct, statusProgress).
5. On success: BackgroundImage = result.Image (Freeze before assigning if not frozen).
6. On OperationCanceledException: swallow.
7. On other exception: Notification.ShowError("Could not load survey image: …"); BackgroundImage = null.
8. Finally: IsBackgroundLoading = false; raise PC.
```

Where it's called:
- `Initialize(target)` — kick off a fire-and-forget load after the existing setup work.
- `SelectedImageSource` setter — persist the new value to `ActiveProfile.FramingAssistantSettings.LastSelectedImageSource`, then load.
- Successful capture path — after `BackgroundFovMultiplier` × captured-FOV is known, re-load.

#### `View/OrbitalFramingWizardView.xaml`

- Add a row to the right-side "Capture" group (above the exposure controls) with `<TextBlock Text="Image Source:"/>` + `<ComboBox ItemsSource="{Binding AvailableImageSources}" SelectedItem="{Binding SelectedImageSource}"/>`.
- The ComboBox's `ItemTemplate` should display NINA's friendly enum descriptions if the source enum is decorated with `[Description]` — reuse the existing `EnumStaticDescriptionValueConverter` already loaded into the plugin's resource dictionary (`Resources/OptionsDataTemplates.xaml`).
- Optional: a small "Loading survey…" badge in the canvas overlay area when `IsBackgroundLoading` is true.

#### `View/OrbitalFramingCanvas.xaml` placeholder visibility

- Currently the "Load an image to begin framing…" `TextBlock` (`OrbitalFramingWizardView.xaml:64-71`) is shown when `HasCapture = false`. Now that the background can render pre-capture, soften the text to something like "Capture an image to enable the framing rectangle" so the user understands the rectangle is intentionally hidden but the survey is informative. Visibility logic stays the same.

#### `ViewModels/OrbitalsVM.cs`

- Update the wizard-construction call site (the place that invokes `wizardFactory()` or builds the VM via DI) to pass `ISkySurveyFactory`. If a `Func<OrbitalFramingWizardVM>` factory is in use, MEF wires up the new dep automatically — just add the `[Import]` field (or constructor param) for `ISkySurveyFactory` on `OrbitalsVM` and let the wizard's `[ImportingConstructor]` resolve.

### 4. Tests — `Tests/ViewModels/OrbitalFramingWizardVMTests.cs`

- Update the `Make(...)` helper and `BlockingCaptureSource` helper-bound test to pass a mocked `ISkySurveyFactory` to the constructor. Default the mock to return a stub `ISkySurvey` whose `GetImage(...)` returns a trivial 1×1 `SkySurveyImage` (or `Task.FromResult(null)` for tests that don't exercise background-loading paths).
- Add one new test: `Initialize_KicksOffBackgroundLoad_UsingBodyCoords` — verify the factory's `Create` is called with the persisted enum value (default = offline atlas) and `GetImage` is invoked with the body's current J2000 coordinates.
- Add one new test: `SelectedImageSource_Change_PersistsToProfile_AndReloads` — set a different source value, assert `ActiveProfile.FramingAssistantSettings.LastSelectedImageSource` is written and `GetImage` is invoked again.

## Critical files

| File | Change |
|---|---|
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml` | Swap `TransformGroup` order on rectangle. Add `ScaleTransform` for `RootGrid.LayoutTransform`. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml.cs` | New `Zoom` DP wired to the `ScaleTransform`. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml` | Wrap canvas in `ScrollViewer`. Add zoom toolbar overlay. Add Image-Source row in Capture group. Soften pre-capture placeholder text. |
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs` | Inject `ISkySurveyFactory`. Add `SelectedImageSource`, `AvailableImageSources`, `IsBackgroundLoading`, `CanvasZoom` + zoom commands. Implement `LoadBackgroundImageAsync`. Wire calls in `Initialize`, capture success, source-change setter. |
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalsVM.cs` | Pull `ISkySurveyFactory` in via `[ImportingConstructor]` so MEF passes it on to the wizard. |
| `NINA.Joko.Plugin.Orbitals.Tests/ViewModels/OrbitalFramingWizardVMTests.cs` | Constructor-signature update across all setup paths. Two new tests for background load + source persistence. |

## Reused utilities

- `NINA.WPF.Base.SkySurvey.ISkySurveyFactory` / `ISkySurvey.GetImage(...)` — fetches survey image.
- `NINA.WPF.Base.SkySurvey.SkySurveySource` enum — full list of sources.
- `NINA.Profile.Interfaces.IFramingAssistantSettings.LastSelectedImageSource` — persisted choice.
- `NINA.Joko.Plugin.Orbitals.Converters.EnumStaticDescriptionValueConverter` (already in `OptionsDataTemplates.xaml`) — pretty enum names in the ComboBox.
- `NINA.Core.Utility.Notification.ShowError(...)` — already used in the VM for failure surfacing.
- `System.Windows.Controls.ScrollViewer` — native zoom + scroll mechanic via `LayoutTransform`.

## Verification

1. `dotnet build NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj -c Debug` succeeds.
2. `dotnet test NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` is green (currently 278 tests; expect ~280 after the two additions).
3. Manual smoke in NINA:
   - **Drag direction.** Open the wizard, load XISF stub, drag the framing rectangle vertically *while rotated to 90°*. The rectangle must slide straight up on screen (no diagonal drift). Confirm the RA/Dec offsets update consistent with the new screen-space drag.
   - **Zoom.** Click "Zoom +" repeatedly: canvas content scales up, scrollbars appear once content exceeds the viewport. Scroll to verify the off-screen survey/captured-image area is reachable. Click "Reset": zoom returns to 1.0, scrollbars disappear.
   - **Image source.** Open the wizard for any orbital target (no capture yet) — within a couple seconds the background survey image appears centered on the body's current position. Change the "Image Source" combo to a different value: the background reloads. Close and reopen NINA's built-in Framing Assistant and verify its image source matches what was last picked in the wizard (proves persistence to the shared profile setting).
   - **Capture flow.** Click "Load Test Image" (XISF stub mode): the captured image overlay appears un-rotated centred on the survey; the survey then re-renders at the captured FOV ×3. Drag + rotate + export as before.

## Out of scope

- Capture-mode work (Live / XISF stub) is unchanged.
- The position-independent (separation, PA) display block is unchanged.
- No new container-side honoring of (sep, PA) — still display-only per the original plan §4.5.
