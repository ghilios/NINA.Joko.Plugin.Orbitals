# Plan: Framing-wizard polish (wizard revisions, round 3)

## Context

Seven new UX-polish items on the just-shipped Orbital Framing Wizard (rounds 1+2 already merged, last commit `b023649`). All scope is inside the wizard; no plumbing changes to capture/export.

1. **Ctrl-scroll should zoom.** Currently mouse-wheel only rotates the rectangle (handled at the rectangle level) or scrolls when the cursor is outside it. The user wants Ctrl+wheel to zoom regardless of cursor location.
2. **Zoom should anchor on the visible center, not the top-left.** Today the LayoutTransform changes scale but the `ScrollViewer` keeps `HorizontalOffset`/`VerticalOffset` at 0, so content slides away.
3. **Auto-stretch the captured image.** `XisfStubCaptureSource` returns the raw XISF as a linear `BitmapSource` via WPF's `BitmapDecoder`. The result is very dark. NINA's standard image pipeline applies auto-stretch (MTF with profile-driven factor + black-point); we should match that.
4. **Image Source belongs in its own section above the Altitude chart**, not buried in the Capture group.
5. **Offline sky map not loading to the framed location.** Either (a) the persisted `LastSelectedImageSource` is something online (HIPS2FITS / NASA) and the offline session shows nothing, or (b) the coordinates being sent are not what the body's J2000 position should be. Mitigation: diagnostic logging + ensure J2000 transform + default initial selection to `SKYATLAS` regardless of profile (keep round-trip persistence on user change).
6. **Zoom buttons in the top-left** (currently top-right).
7. **Visible area = 2× the captured-image height, and the rectangle can roam off-screen.** Replace the current `Math.Min(w/(pxW·fov), h/(pxH·fov))` scaling with height-only sizing at multiplier `2.0`. Remove `ClipToBounds="True"` on the canvas root so the rectangle stays visible up to the ScrollViewer's viewport edges, even when only partially overlapping the captured frame.

## Approach

One file per concern in most cases; auto-stretch (item 3) is the only one that touches a capture source. All zoom logic moves into the view code-behind so it can talk to the `ScrollViewer` directly.

### 1 + 2 + 6. Zoom: center-anchored, Ctrl+wheel, top-left buttons

**Touch**: `View/OrbitalFramingWizardView.xaml(.cs)`, `ViewModels/OrbitalFramingWizardVM.cs`.

- In the VM, change the `CanvasZoom` setter from `private set` to public (or expose `ApplyZoomFactor(double factor)` and `ResetZoom()` helpers that clamp into `[MinZoom, MaxZoom]` and assign). Drop the existing `ZoomInCommand`/`ZoomOutCommand`/`ResetZoomCommand` properties — the view drives zoom now.
- In `OrbitalFramingWizardView.xaml`:
  - Move the zoom toolbar `StackPanel` to `HorizontalAlignment="Left"`.
  - Replace the `Command="..."` bindings on the three buttons with `Click="ZoomIn_Click"` / `ZoomOut_Click` / `ZoomReset_Click` (named handlers).
  - Add `PreviewMouseWheel="Canvas_PreviewMouseWheel"` on the `ScrollViewer` (`x:Name="CanvasScroll"`).
- In `OrbitalFramingWizardView.xaml.cs`:
  - Implement the click + wheel handlers. All three button handlers call a single `ZoomByFactor(double factor)`; reset calls `ZoomTo(1.0)`.
  - `Canvas_PreviewMouseWheel` checks `Keyboard.Modifiers.HasFlag(ModifierKeys.Control)` — if so, `e.Handled = true` and call `ZoomByFactor(e.Delta > 0 ? ZoomStep : 1/ZoomStep)`. Without Ctrl: let it bubble (rectangle wheel handler or ScrollViewer scroll handles it as today).
  - The recentering math:
    ```
    sv = CanvasScroll;
    oldExtentW = sv.ExtentWidth;       oldExtentH = sv.ExtentHeight;
    oldHO      = sv.HorizontalOffset;  oldVO      = sv.VerticalOffset;
    vpW = sv.ViewportWidth;            vpH = sv.ViewportHeight;
    // fractional center of the viewport in the current extent
    fx = oldExtentW > 0 ? (oldHO + vpW / 2.0) / oldExtentW : 0.5;
    fy = oldExtentH > 0 ? (oldVO + vpH / 2.0) / oldExtentH : 0.5;
    vm.CanvasZoom = newZoom;
    Dispatcher.BeginInvoke(new Action(() => {
        var nw = sv.ExtentWidth; var nh = sv.ExtentHeight;
        sv.ScrollToHorizontalOffset(fx * nw - sv.ViewportWidth / 2.0);
        sv.ScrollToVerticalOffset  (fy * nh - sv.ViewportHeight / 2.0);
    }), DispatcherPriority.Loaded);
    ```
    `DispatcherPriority.Loaded` ensures the LayoutTransform has been applied and `ExtentWidth/Height` reflect the new size before we re-scroll.

### 3. Auto-stretch the captured image — `Imaging/XisfStubCaptureSource.cs`

- Inject `IImageDataFactory` into `XisfStubCaptureSource` (add to its `[ImportingConstructor]`). The factory is already MEF-exported by NINA (we now also import it into `OrbitalsVM` for the `SkySurveyFactory` construction).
- Replace the current `BitmapDecoder.Create(...)` path with NINA's full XISF pipeline:
  - `using NINA.Image.FileFormat.XISF;` then `var imageData = await XISF.Load(new Uri(path), imageStatisticsRequired: true, imageDataFactory, ct);` *(verify the exact signature at compile time — older NINAs name it `XISF.Load(Uri, bool, IImageDataFactory, CancellationToken)`)*.
  - Render: `var rendered = imageData.RenderImage();`
  - Stretch with the profile's auto-stretch settings: pull `AutoStretchFactor` and `BlackClipping` from `ActiveProfile.ImageSettings`, then `rendered = await rendered.Stretch(factor, blackPoint, unlinked: false);`
  - `bitmap = rendered.Image; bitmap.Freeze();`
- Keep the existing synthetic-fallback path (used when the stub XISF is missing) untouched.
- `LiveCaptureSource` is unchanged — it already returns NINA-prepared images from `IImagingMediator.ImagePrepared`.
- The exact stretch method name and parameter order may differ slightly across NINA versions; if the call shape above doesn't compile, fall back to `ImageAnalysis.Stretch(rendered.RawImageData, ...)` or use `rendered.GetImageProperties()` to pull statistics and apply MTF manually.

### 4. UI reorganization — Image Source above Altitude

In `View/OrbitalFramingWizardView.xaml`:

- Delete the `Image Source` row (label + `ComboBox`) inside the existing `<GroupBox Header="Capture">`.
- Restore the Capture grid back to 3 rows (Exposure / Gain / Offset).
- Insert a new `<GroupBox Header="Image Source" Margin="0,0,0,5">` directly **before** the `Altitude` GroupBox. It contains the same `ComboBox` (bound to `AvailableImageSources` / `SelectedImageSource`) plus the same tooltip text. Optionally use the `EnumStaticDescriptionValueConverter` already loaded in plugin resources for prettier item labels.

### 5. Offline sky-map fix — `ViewModels/OrbitalFramingWizardVM.cs`

Three changes:

1. **Default to `SKYATLAS` regardless of profile**, while keeping write-through. `LoadInitialImageSource()` currently reads from `FramingAssistantSettings.LastSelectedImageSource`. Replace with a hard `return SkySurveySource.SKYATLAS;` (and write that value back to the profile on first load if it differed) **OR** keep reading the profile but flip the value to `SKYATLAS` when it equals NINA's default-which-needs-internet. Pick the simpler option: always seed `selectedImageSource = SkySurveySource.SKYATLAS`. The setter's existing persistence path means any subsequent user change still round-trips to the profile.
2. **Force J2000 epoch** before handing to the survey. In `ReloadBackgroundAsync`, replace the current `coords = selectedObject.PositionAt(DateTime.UtcNow).Coordinates;` (and the post-capture branch's `coords = CapturedImageCoordinates`) with `coords = <expr>.Transform(Epoch.J2000);` so the survey API gets the epoch it expects regardless of what the orbital container reports.
3. **Diagnostic logging.** Add `Logger.Info($"Fetching survey: source={SelectedImageSource}, name='{name}', ra={coords.RA:F6}, dec={coords.Dec:F6}, fov={fovArcmin:F2}arcmin");` immediately before the `await survey.GetImage(...)` call so a future "still wrong" report is debuggable from `NINA.log`.

### 7. Visible area = 2× height + roaming rectangle

**Touch**: `View/OrbitalFramingCanvas.xaml(.cs)`, `ViewModels/OrbitalFramingWizardVM.cs`.

- VM: change the `BackgroundFovMultiplier` initialization from `3.0` to `2.0`.
- Canvas XAML (`OrbitalFramingCanvas.xaml:18`): remove `ClipToBounds="True"` from the root `<Grid x:Name="RootGrid">`.
- Canvas code-behind: in `UpdateCapturedLayerSize`, change the post-capture sizing to height-only:
  ```csharp
  double scale = h / (pxH * fovMultiplier);     // height-driven
  double imgW = pxW * scale;
  double imgH = pxH * scale;
  ```
  Width still derives from aspect ratio. If `pxW * scale > w` (very wide sensor), the captured image will be wider than the viewport — that's fine; the ScrollViewer surfaces horizontal scrollbars when zoom > 1, and at zoom=1 the user sees the central viewport-width strip.

## Critical files

| File | Change |
|---|---|
| `View/OrbitalFramingWizardView.xaml` | Zoom buttons → top-left + `Click` handlers; `PreviewMouseWheel` on `ScrollViewer`; Image-Source row removed from Capture group; new `Image Source` GroupBox above Altitude. |
| `View/OrbitalFramingWizardView.xaml.cs` | Zoom button click handlers + Ctrl+wheel handler with center-preserving scroll math. |
| `View/OrbitalFramingCanvas.xaml` | Drop `ClipToBounds="True"` from RootGrid. |
| `View/OrbitalFramingCanvas.xaml.cs` | `UpdateCapturedLayerSize` → height-only scaling. |
| `ViewModels/OrbitalFramingWizardVM.cs` | Public/settable `CanvasZoom` (or `ApplyZoomFactor` helper); drop `ZoomIn/Out/Reset` commands; default `BackgroundFovMultiplier = 2.0`; `LoadInitialImageSource` hard-defaults to `SKYATLAS`; `ReloadBackgroundAsync` transforms to J2000 + logs request params. |
| `Imaging/XisfStubCaptureSource.cs` | Inject `IImageDataFactory`; load XISF via NINA's pipeline; apply auto-stretch using `ActiveProfile.ImageSettings.AutoStretchFactor` + `BlackClipping`. |

## Reused utilities

- `NINA.Image.FileFormat.XISF.XISF.Load(...)` — XISF reader returning `IImageData`.
- `IRenderedImage.Stretch(double factor, double blackClipping, bool unlinked)` — NINA's auto-stretch.
- `ActiveProfile.ImageSettings.AutoStretchFactor` / `BlackClipping` — user's preferred stretch defaults.
- `NINA.Astrometry.Coordinates.Transform(Epoch)` — J2000 conversion.
- `System.Windows.Controls.ScrollViewer.ScrollToHorizontalOffset/VerticalOffset` — re-anchoring.
- `Keyboard.Modifiers` — Ctrl detection in the wheel handler.

## Verification

1. `dotnet build NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj -c Debug` succeeds.
2. `dotnet test NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` is green (still 280 — no test changes expected; the only VM-public surface that changes is `CanvasZoom`'s setter visibility, which existing tests don't touch).
3. Manual smoke in NINA:
   - **Ctrl+wheel.** Open wizard, load XISF stub, scroll wheel without Ctrl over canvas: rectangle rotates (existing behavior); with Ctrl held: zoom in/out smoothly. Cursor over rectangle vs. over background should both zoom.
   - **Center-preserving zoom.** Pan to a non-center spot via scrollbars at zoom=2, then zoom in further: the same visual content stays in view (no top-left snap).
   - **Auto-stretch.** XISF stub now renders bright with star detail visible (matches what NINA's image viewer would show for the same file).
   - **Image Source placement.** Combo appears in its own GroupBox above Altitude, no longer in Capture group.
   - **Sky map at body location.** Open wizard for any comet/asteroid → background survey image is centered on the body's actual RA/Dec (eyeballed against NINA's framing-assistant). Check `NINA.log` to confirm the logged coords match the expected body position.
   - **Top-left zoom buttons.** Buttons visually anchor in the top-left of the canvas.
   - **2× height + roaming rectangle.** Visible vertical area equals 2× the captured frame's height. Drag the rectangle to the far edge of the visible area: the rectangle should remain visible as it extends partially off the captured image, only being clipped by the ScrollViewer's viewport (not by the canvas's RootGrid).
4. Export to Sequencer still works — capture → drag → export → confirm orbital container receives correct offsets + PA (no change to that path).

## Side task — `CLAUDE.md` plans-folder note

Separate from the wizard work but to be done in the same approval: add a one-line directive to `CLAUDE.md` stating that plan-mode plan files should live in the project's `plans/` directory (not `~/.claude/plans/`). This codifies the existing user preference so future sessions don't have to be reminded.

## Out of scope

- Capture-mode (Live / XisfStub) routing — unchanged.
- The (separation, PA) sky-frame offset display — unchanged.
- Any container-side honoring of sky-frame offsets — still display-only, deferred.
- Mouse-anchored zoom (zoom toward the cursor) — viewport-center is good enough; revisit if requested.
