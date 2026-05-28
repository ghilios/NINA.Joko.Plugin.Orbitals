# Plan: Sensor-frame display + sensor-aspect rectangle (Orbital Framing Wizard revisions)

## Context

Two pieces of UX feedback on the freshly-shipped wizard (PR #18, branch `plan/orbital-framing-wizard`):

1. **Rectangle should match the captured image dimensions.** Today the framing rectangle is sized to the canvas's `1/fovMultiplier × 1/fovMultiplier` layout box (a square cell), regardless of the captured image's actual aspect ratio. On any non-square sensor (4:3, 16:9) the rectangle drifts off the image footprint. The fix is to size both the captured-image container *and* the rectangle from `CapturedImageWidthPx × CapturedImageHeightPx` so they are bit-for-bit congruent on screen.

2. **Image should render un-rotated; rectangle rotation is a delta from the plate-solved PA.** Today the captured image is rotated by `CapturedImageRotation` so survey-north is up. The user wants the image in its native sensor frame, with the rotation slider expressing "how much do I want to deviate from the camera's actual PA?" — slider at 0 means "frame as captured, no rotation offset", and the displayed final PA equals the sequencer-inverted plate-solved PA.

A side benefit: the offset math becomes self-consistent. Today `RecalculateOffsets` calls `Coordinates.Shift(dx_canvasPx, dy_canvasPx, CapturedImageRotation, pixscale, pixscale)` — but `pixscale` is arcsec **per captured-image pixel** while `dx/dy` arrive as **canvas pixels** (`OrbitalFramingCanvas.xaml.cs:121-131`, `:245-257`). After this change, drag deltas are converted to image pixels at the source (in the canvas), so the units line up and the existing `Shift(...)` call becomes correct without further changes.

## Approach

Four production files, one test file. No new files.

### 1. `View/OrbitalFramingCanvas.xaml(.cs)` — un-rotate image, size rectangle from image pixels

- Remove the `RotateTransform x:Name="CapturedRotation"` from the captured-image layer's `RenderTransform` (`OrbitalFramingCanvas.xaml:33-35`). The image renders in its native sensor frame.
- Drop the `CapturedImageRotation` → `CapturedRotation.Angle` callback (`OrbitalFramingCanvas.xaml.cs:160-163`). Keep the `CapturedImageRotation` DP itself so the VM still has a binding target for its final-PA math (the VM reads it; the canvas no longer renders with it). Note in the DP doc comment that the canvas does not apply this visually — it is a pass-through used by the VM only.
- Add two new DPs on the canvas: `CapturedImagePixelWidth` and `CapturedImagePixelHeight` (double, default 0).
- Replace `UpdateCapturedLayerSize` (`OrbitalFramingCanvas.xaml.cs:198-214`) so the captured-image host and the framing rectangle share a computed size derived from those new DPs:
  ```
  if (CapturedImagePixelWidth <= 0 || CapturedImagePixelHeight <= 0) {
      // pre-capture fallback: keep current behavior
  } else {
      scale = min(canvasW / (CapturedImagePixelWidth  * fovMultiplier),
                  canvasH / (CapturedImagePixelHeight * fovMultiplier));
      capturedW = CapturedImagePixelWidth  * scale;
      capturedH = CapturedImagePixelHeight * scale;
      rectangleW = capturedW;   // exactly match the captured image's footprint
      rectangleH = capturedH;
  }
  ```
  Store `scale` on the control so the drag handler can convert.
- Update the mouse-drag handler (`OrbitalFramingCanvas.xaml.cs:245-257`): convert canvas-pixel deltas to captured-image-pixel deltas via `dx_img = dx_canvas / scale`, write those into `RectangleOffsetX/YPx`. Update the DP doc comments to say "captured-image pixels" instead of "canvas pixels".
- The captured image's `Stretch` can stay `Uniform` — when the host's aspect equals the source's aspect it behaves identically to `Fill`.

### 2. `View/OrbitalFramingWizardView.xaml` — bind the new DPs, relabel the slider

- Add bindings on `<local:OrbitalFramingCanvas …>`:
  ```xml
  CapturedImagePixelWidth="{Binding CapturedImageWidthPx}"
  CapturedImagePixelHeight="{Binding CapturedImageHeightPx}"
  ```
  (`OrbitalFramingWizardView.xaml:52-61` is the canvas declaration.)
- Reword the rotation slider label (`OrbitalFramingWizardView.xaml:203-205`) to make the delta semantics explicit, e.g. `"Rotation Δ (offset from captured-image PA — drag rectangle or use slider):"`.
- Keep the slider range 0–360. The "Final Sequencer Position Angle" block already shows the resolved value; the user sees the relationship live as they drag.

### 3. `ViewModels/OrbitalFramingWizardVM.cs` — confirm property accessibility, leave math alone

- Ensure `CapturedImageWidthPx` and `CapturedImageHeightPx` are public `INPC` properties (they exist as `CapturedFrame` fields today; just confirm the VM surfaces them with `RaisePropertyChanged` on capture).
- `RecalculateOffsets` (currently around `OrbitalFramingWizardVM.cs:276-312`): no formula change. It already passes `CapturedImageRotation` as the rotation argument to `Coordinates.Shift`. With drag deltas now in image-pixel units and `pixscale` in arcsec/image-pixel, the call is finally unit-consistent. Update the local doc comment to record what units `RectangleOffsetXPx/YPx` carry post-revision.
- `FinalPositionAngle` (currently around `OrbitalFramingWizardVM.cs:667`): no change. The formula `((360 − (CapturedImageRotation + RectangleRotationDeg)) mod 360 + 360) mod 360` already produces the desired semantics under the new interpretation (slider at 0 ⇒ PA = sequencer-inverted plate-solved PA; slider at Δ ⇒ PA shifts by Δ).

### 4. `Tests/ViewModels/OrbitalFramingWizardVMTests.cs` — adjust pixel-unit expectations

- The natural-rotation `FinalPositionAngle` test (currently around lines 239–244) keeps passing — same formula, same expected value at `RectangleRotationDeg = 0`.
- The reset-framing test (currently lines 250–274) keeps passing.
- Any test that drives `RectangleOffsetXPx/YPx` and asserts RA/Dec offset values needs its expected numbers recomputed: input deltas now represent **image pixels** (not canvas pixels). The new expected values come from `Coordinates.Shift(dx_imgPx, dy_imgPx, CapturedImageRotation, pixscale, pixscale)` invoked directly — which is what the production code does, so a test that mirrors the production call shape will pass trivially.
- Add a small assertion that, for a sweep of `(CapturedImageRotation, RectangleRotationDeg)` pairs that sum to the same value, `FinalPositionAngle` is identical — locking in the "delta" semantics.

## Critical files

| File | Change |
|---|---|
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml` | Remove captured-image `RotateTransform`; sizing layout binds to new DPs. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml.cs` | Drop rotation callback; add `CapturedImagePixelWidth/Height` DPs; rewrite `UpdateCapturedLayerSize`; convert drag deltas to image pixels. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml` | Wire new DPs; relabel rotation slider. |
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs` | Doc-comment refresh; confirm `CapturedImageWidthPx/HeightPx` surface; no math change. |
| `NINA.Joko.Plugin.Orbitals.Tests/ViewModels/OrbitalFramingWizardVMTests.cs` | Update offset-unit assertions; add delta-invariance test for `FinalPositionAngle`. |

## Reused utilities

- `NINA.Astrometry.Coordinates.Shift(dx, dy, rotationDeg, scaleX, scaleY)` — unchanged usage; deltas now arrive in matching units.
- Existing rectangle drag/scroll/slider gestures in `OrbitalFramingCanvas.xaml.cs` — only the unit of measure transmitted out changes.

## Verification

1. `dotnet build NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj -c Debug` succeeds and post-build deploy still copies the DLL to `%localappdata%\NINA\Plugins\3.0.0\Orbitals`.
2. `dotnet test NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` is green; the unit-correction test edits are minimal.
3. Manual smoke in NINA against the XISF stub:
   - Captured image renders **un-rotated** (not tilted to sky-north).
   - Framing rectangle outlines the captured image **exactly** — same aspect ratio, same on-screen extent.
   - Drag the rectangle by N image pixels in the sensor +X direction → RA offset changes by approximately `pixscale × N × sec(Dec)` hours-worth (sanity-checkable for a couple of drag amounts).
   - Rotation slider at 0 → `Final Sequencer Position Angle` equals `(360 − CapturedImageRotation) mod 360`.
   - Rotation slider at +Δ → `Final Sequencer Position Angle` shifts by exactly +Δ (mod 360, with sequencer's sign convention).
4. Export to Sequencer → orbital container receives the same offsets and PA shown in the wizard's offsets/PA panel. (No export-path changes.)
