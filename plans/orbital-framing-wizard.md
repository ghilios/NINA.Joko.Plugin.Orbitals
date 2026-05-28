# Plan: Orbital Framing Wizard

## Context

The Orbitals plugin lets users image moving bodies (comets, asteroids, satellites, JWST, planets). Today, the Frame button on `OrbitalsVM` hands the object's current RA/Dec to NINA's built-in `FramingAssistantVM`, which has no concept of orbital motion: it treats the target as a static DSO. Two consequences:

1. The framing the user picks is only valid at that instant — by the time the sequencer runs, the body has moved.
2. There is no way to compose a comet/asteroid shot with the head of the body intentionally off-center (e.g., to leave room for a tail) while still letting orbital tracking continue to follow the body.

The Orbitals sequence containers (`OrbitalObjectContainer`, `SolarSystemBodyContainer`, `ManualTLEContainer`, `JWSTContainer`) already support an `OffsetCoordinates` (RA/Dec relative to the body) and a `Target.PositionAngle` (rotator angle). They will correctly apply both as the body moves. What's missing is a visual tool to *choose* those offsets and rotation against a real image of the field.

**Outcome:** A new Orbital Framing Wizard, launched from the existing Frame button, that:
- Slews to the body's current position, captures an exposure, plate-solves it.
- Displays the captured image overlaid (rotated to match) on a sky-survey background ~3× the image FOV.
- Lets the user pan/rotate a rectangle the same size as the captured image to choose the framing.
- Computes RA/Dec offsets (body-relative) and a final position angle.
- Exports those offsets + rotation into the right `OrbitalsContainerBase<T>` subclass in the Advanced Sequencer.

## Approach

### 0. Delivery sequence

The work is broken into five phases. **Each phase ships as two commits on the same feature branch: an implementation commit followed by a code-review-and-fix commit** that runs the `/code-review` skill (or `/ultrareview` if a broader review is warranted) against the implementation commit's diff and addresses every finding in a follow-up commit before the next phase begins. The phase is not considered complete until that second commit is in. No phase starts until the previous phase's review-fix commit is merged into the working branch.

The implementation reference for each phase lives in §§1–10 below; the phases just sequence those building blocks.

| Phase | Deliverables | Commits |
|---|---|---|
| **A — Math + abstractions** | `OrbitalOffsetMath.cs` (§4.5), `ICaptureSource` + `CapturedFrame` (§3.1), unit tests in §§9.1–9.3, §9.6 (real-target equivalence tests). No UI, no MEF wiring yet. | (1) implementation • (2) code review + fix |
| **B — Wizard VM + XISF stub** | `OrbitalFramingWizardVM` (§2) with all commands and computed-property bindings, `XisfStubCaptureSource` (§3.2), `OrbitalsVM.SendToFramingWizardCommandAction` rewired (§7) to open the new window, VM unit tests §9.4 against a `FakeCaptureSource`. Wizard window XAML can be a thin placeholder at this stage — bindings exposed but layout minimal. | (1) implementation • (2) code review + fix |
| **C — UI: canvas + altitude chart + offsets display** | `OrbitalFramingWizardView` (§1) fully laid out, `OrbitalFramingCanvas` (§4) with layered background/captured-image/rectangle, altitude chart embedded (§5), both offset representations rendered (§4.5, §5). Manual smoke test against the static XISF: load → pan → rotate → see RA/Dec offsets *and* the new separation+PA pair update live. | (1) implementation • (2) code review + fix |
| **D — Sequencer export** | Container resolution, clone-and-populate, `AddAdvancedTarget`, navigate to Sequencer tab (§6). Export unit tests §9.5. After this phase the entire end-to-end workflow described in the Context section is functional against the static XISF. | (1) implementation • (2) code review + fix |
| **E — `LiveCaptureSource`** | `LiveCaptureSource` (§3.3), `OrbitalsOptions.CaptureMode` enum + persistence, multi-source MEF resolution and button-label switching in the VM. Live equipment verification in §10. Nothing about live equipment is built before this phase. | (1) implementation • (2) code review + fix |

The phase boundaries also align with natural test surface: A is pure math/abstraction with deep unit coverage; B brings the VM under test with fakes; C is the first visual milestone; D closes the loop to the sequencer; E adds the equipment integration last so it never blocks the earlier visual / interaction work.

### 1. Hosting: Modal WPF Window

- New `OrbitalFramingWizardView : Window` opened via `IWindowServiceFactory` from `OrbitalsVM`'s Frame command, replacing the current `applicationMediator.ChangeTab(FRAMINGASSISTANT) + framingAssistantVM.SetCoordinates(dso)` flow.
- Window is sized generously (e.g., 1400×900) since it hosts a layered image canvas, altitude chart, summary, and exposure controls.
- Closed explicitly by Cancel or by successful Export (which also navigates the user to the Sequencer tab via `IApplicationMediator.ChangeTab(ApplicationTab.SEQUENCE)`).

### 2. New ViewModel: `OrbitalFramingWizardVM`

**Location:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs`

**MEF registration:** `[Export(typeof(OrbitalFramingWizardVM))]` with `[PartCreationPolicy(CreationPolicy.NonShared)]` so each invocation gets a fresh instance. Resolved into `OrbitalsVM` via `[ImportingConstructor]` (additional injected dep) or via `CompositionContainer` lookup; choose the former for consistency with the rest of this plugin.

**Constructor-injected dependencies (all public NINA SDK interfaces — the VM never depends on live-equipment mediators directly; those land on `LiveCaptureSource` in Phase E):**
- `IProfileService` — pixel size, focal length, plate-solve and framing-assistant settings (image source enum, opacity default).
- `IEnumerable<Lazy<ICaptureSource, ICaptureSourceMetadata>>` — all registered capture sources; VM picks one by `OrbitalsOptions.CaptureMode`. In Phases B–D only `XisfStubCaptureSource` is registered. In Phase E `LiveCaptureSource` is added; its own constructor pulls in the equipment mediators (`ICameraMediator`, `IImagingMediator`, `ITelescopeMediator`, `IFilterWheelMediator`, `IDomeMediator`, `IDomeFollower`, `IPlateSolverFactory`).
- `ISkySurveyFactory` — fetches the background image.
- `INighttimeCalculator` — drives the altitude chart.
- `ISequenceMediator` — `GetDeepSkyObjectContainerTemplates`, `AddAdvancedTarget`.
- `IApplicationMediator` — switch to Sequencer tab on export.
- `IApplicationStatusMediator` — progress notifications.

**Initialization handle:** `void Initialize(OrbitalsObjectBase selectedObject)` called once before the window is shown. Internally:
- Stores `selectedObject` (typed as `OrbitalsObjectBase`).
- Calls `nighttimeCalculator.Calculate()` for the altitude chart.
- Starts a low-frequency live-refresh `Ticker` (≈2 s) that recomputes `CurrentPosition / CurrentTrackingRate / MaxExposureSeconds` from `selectedObject.PositionAt(DateTime.UtcNow)`. Reuse `OrbitalsVM.MaxExposureSeconds` derivation logic — extract that math into a static helper in `OrbitalsObjectBase` or in `NINA.Joko.Plugin.Orbitals/Utility` so both VMs share it.

**Properties (bind targets):**
- Summary: `Name`, `CurrentRAString`, `CurrentDecString`, `RATrackingRate`, `DecTrackingRate`, `Distance`, `MaxExposureSeconds`.
- Exposure: `ExposureTime`, `Gain`, `Offset`, `Binning`, `SelectedFilter`. Defaults from `ActiveProfile.PlateSolveSettings`.
- Image source: `FramingAssistantSource` (`SkySurveySource` enum). Default from `ActiveProfile.FramingAssistantSettings.LastSelectedImageSource`.
- Background FOV multiplier: `BackgroundFovMultiplier` (default `3.0`, range 1.5–5.0).
- Captured-image state: `CapturedImage` (`BitmapSource`), `CapturedImageRotation` (deg from plate solve), `CapturedImagePixscale` (arcsec/pixel from plate solve), `CapturedImageCoordinates` (`Coordinates` from plate solve), `CapturedImageWidthPx`, `CapturedImageHeightPx`.
- Background: `BackgroundImage` (`SkySurveyImage`).
- Rectangle: `Rectangle` (`FramingRectangle` from `NINA.Astrometry`).
- Computed offsets: `RAOffsetHours` (double, signed), `DecOffsetDegrees` (double, signed), `FinalPositionAngle` (0–360 deg). All recompute on `Rectangle` property changes.
- Status flags: `IsCapturing`, `HasCapture`, `IsExporting`.
- `NighttimeData`.

**Commands:**
- `SlewCenterAndImageCommand` (`IAsyncCommand`) — see step 3.
- `CancelCaptureCommand`.
- `ResetFramingCommand` — reset rectangle to center, rotation to 0.
- `ExportToSequencerCommand` (`IAsyncCommand`) — see step 6.
- `CancelCommand` — close window.

### 3. Capture-and-display command

**Delivery order (see §0):** the XISF stub source in §3.2 is built first and the entire wizard (pan/rotate/offsets/export) is proven end-to-end against it (Phases A–D). `LiveCaptureSource` in §3.3 is the last phase (Phase E), added only after the static-XISF flow is fully working.

#### 3.1 Capture abstraction

The button populates the captured-image state (`CapturedImage`, `CapturedImageCoordinates`, `CapturedImageRotation`, `CapturedImagePixscale`, `CapturedImageWidthPx/HeightPx`) so the canvas in §4 can render. Everything downstream — rectangle pan/rotate, offset math, sequencer export — is identical regardless of where that state comes from. The VM depends on a small abstraction:

```csharp
internal interface ICaptureSource {
    Task<CapturedFrame> CaptureAsync(
        OrbitalsObjectBase target,
        OrbitalFramingExposureSettings exposure,
        IProgress<ApplicationStatus> progress,
        CancellationToken ct);
}

internal sealed class CapturedFrame {
    public BitmapSource Image { get; init; }
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
    public Coordinates Coordinates { get; init; }   // image center, J2000
    public double PositionAngleDeg { get; init; }   // 0..360
    public double PixscaleArcsecPerPx { get; init; }
}
```

This abstraction exists primarily so the VM is unit-testable with a fake source, and secondarily so the future live-capture work item can drop in a sibling implementation without touching the VM. The plan deliberately ships only one implementation — the XISF stub.

#### 3.2 `XisfStubCaptureSource` — the only implementation in this plan

**Sample file (provided by user):**

`E:\AP\processing\1_selected\LIGHT_2024-10-26_22-16-36_L_-10.00_120.00s_0069_c_cc_a.xisf`

**Implementation** (`NINA.Joko.Plugin.Orbitals/Imaging/XisfStubCaptureSource.cs`):

1. Load the XISF bitmap. Use the public SDK loader (`NINA.Image.ImageData.FromFile(path, ...)` / `XISF.Load(...)` — confirm the exact entry point during implementation; both live in `NINA.Image`, transitively referenced via `NINA.Plugin`). Take only the `BitmapSource` and the pixel dimensions; **ignore** any plate-solve metadata embedded in the XISF headers (per user direction).
2. `CapturedFrame.Coordinates = target.PositionAt(DateTime.UtcNow).Coordinates`. The orbital object's current J2000 position becomes the image center; no slew is issued, no mount touched.
3. `CapturedFrame.PositionAngleDeg = _rng.NextDouble() * 360.0`. A new random orientation per invocation so the wizard exercises the captured-image rotation transform and the offset-math handling of non-axis-aligned frames. Seed is fresh each call (no determinism required; tests use `FakeCaptureSource` instead).
4. `CapturedFrame.PixscaleArcsecPerPx` — derive from the active profile's `Camera.PixelSize` and `Telescope.FocalLength` (`arcsec_per_pixel = 206.265 × pixel_size_µm / focal_length_mm`). If either is zero/missing, fall back to `1.0 arcsec/px` and log a warning; the wizard must still run against a fresh profile.
5. `progress.Report(...)` a few steps so the existing status UI populates ("Loading frame…", "Decoding…", "Done").
6. Path is a `const` in the stub class. If the file is missing, surface a clear `Notification.ShowError("XISF stub frame not found at <path>")` and return; the VM keeps `HasCapture = false`.

**Registration.** `[Export(typeof(ICaptureSource))]` so MEF resolves it as the single registered source. The VM injects `IEnumerable<ICaptureSource>` and uses `Single()`. When a future live-capture work item adds a sibling export, that PR introduces the selection mechanism — not this one.

No camera, mount, filter wheel, dome, guider, plate-solver, or sequencer-capture path is reachable through this code in the scope of this plan. The button label reads "Load Test Image" (`CaptureButtonLabel` property on the VM, hardcoded for now; the future live work will swap it to "Slew, Center & Image").

#### 3.3 `LiveCaptureSource` — delivered as the final phase (see §0 Phase E)

Once Phases A–D have shipped and the wizard is proven end-to-end against the static XISF, add `LiveCaptureSource : ICaptureSource` (file `NINA.Joko.Plugin.Orbitals/Imaging/LiveCaptureSource.cs`):

1. **Pre-flight checks.** `ICameraMediator.GetInfo().Connected` and `ITelescopeMediator.GetInfo().Connected` — surface a clear notification and bail otherwise. Read current camera info for pixel size; fall back to profile when the camera doesn't report it.
2. **Compute target coordinates** = `target.PositionAt(DateTime.UtcNow).Coordinates` (J2000).
3. **Run `ICenteringSolver.Center(...)`** built from `IPlateSolverFactory.GetCenteringSolver(plateSolver, blindSolver, imagingMediator, telescopeMediator, filterWheelMediator, domeMediator, domeFollower)`. Pass:
   - `CaptureSequence` from the VM's exposure settings (image type `SNAPSHOT`).
   - `CenterSolveParameter` populated from `ActiveProfile.PlateSolveSettings` plus the target coordinates from step 2.
   - Cancellation token wired to `CancelCaptureCommand`.

   Sidereal tracking is left as the mount's default — no custom orbital rate is set for the framing capture (per user decision; trailing is acceptable for the framing exposure since plate solving relies on stars). The orbital tracking rate displayed in the summary panel is informational only.

4. **Capture the displayed image.** `ICenteringSolver.Center` returns a `PlateSolveResult` but does not surface the final image bitmap directly. Preferred approach: subscribe to `IImagingMediator.ImagePrepared` before invoking the solver, hold the last `IRenderedImage` of the run, and unsubscribe in a `finally`. Fallback: one additional `IImagingMediator.CaptureAndPrepareImage(...)` cycle if the event-based path proves unreliable.

5. **Return `CapturedFrame`** from `PlateSolveResult.Coordinates / PositionAngle / Pixscale` plus the captured `IRenderedImage.Image` and bitmap dimensions.

6. **Registration & selection.** Add `[Export(typeof(ICaptureSource))]` on `LiveCaptureSource` and an `[ExportMetadata("Mode", ...)]` on both implementations (`XisfStub`, `Live`). Introduce `OrbitalsOptions.CaptureMode` (persisted enum, default `Live` for end-users; `XisfStub` available for QA/devs). The VM resolves `IEnumerable<Lazy<ICaptureSource, ICaptureSourceMetadata>>` and picks by `OrbitalsOptions.CaptureMode`. Update the `CaptureButtonLabel` accordingly ("Slew, Center & Image" for `Live`; "Load Test Image" for `XisfStub`).

**Equipment dependencies (`IImagingMediator`, `ICameraMediator`, `IFilterWheelMediator`, `IDomeMediator`, `IDomeFollower`, `IPlateSolverFactory`, `ITelescopeMediator`) land only on `LiveCaptureSource`'s constructor — the wizard VM itself never injects them.** This isolation is non-negotiable: the test path for the VM stays mediator-free.

#### 3.4 Common post-capture steps (in the VM)

After `captureSource.CaptureAsync` returns:

a. **Persist captured state** onto the VM properties.
b. **Fetch background sky-survey image:** `imageArcminWidth = pixscale × widthPx / 60`; `backgroundFov_arcmin = imageArcminWidth × BackgroundFovMultiplier`. Call `ISkySurveyFactory.Create(FramingAssistantSource).GetImage(name, CapturedImageCoordinates, backgroundFov_arcmin, backgroundPxWidth, backgroundPxHeight, ct, progress)`.
c. **Initialize rectangle** centered on the captured image, width/height = captured image at canvas pixel scale, `Rectangle.Rotation = 0`, `Rectangle.OriginalCoordinates = CapturedImageCoordinates`, `Rectangle.DSOPositionAngle = CapturedImageRotation`.
d. `HasCapture = true`. The rest of the wizard (pan/rotate/offset/export) is fully usable from here without any further equipment interaction.

### 4. Framing canvas: `OrbitalFramingCanvas` UserControl

**Location:** `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml(.cs)`

A bespoke layered control (it is simpler to ship our own than to retrofit NINA's mosaic-aware `FramingAssistantView`). Layered top-to-bottom inside a `Grid` whose size = `Rectangle.OriginalWidthPx × BackgroundFovMultiplier`:

1. **Background layer** — `Image Source="{Binding BackgroundImage.Image}"` filling the full canvas with `RenderTransform` `RotateTransform(InverseBackgroundRotation)` so survey "north" is up-ish, matching how `FramingAssistantView.xaml` uses `ImageView.ImageRotation="{Binding InverseRectangleRotation}"`.
2. **Captured-image layer** — `Image Source="{Binding CapturedImage}"` centered in the canvas, sized at 1× the captured-image canvas size (i.e., 1/BackgroundFovMultiplier of the grid), `RenderTransform` `RotateTransform(CapturedImageRotation)` so it aligns to the survey background. Use a slight transparency (`Opacity ≈ 0.85`) so the user can see it sit on the survey.
3. **Rectangle layer** — single `Rectangle` with `Stroke="{StaticResource ButtonForegroundBrush}"` from NINA.WPF.Base, with:
   - `behaviors:DragCommandBehavior.DragMoveCommand="{Binding DragMoveCommand}"` etc., reusing the same behaviors NINA's framing uses (in `NINA.WPF.Base`).
   - `behaviors:MouseWheelCommandBehavior.MouseWheelCommand="{Binding RotateRectangleCommand}"` repurposed so wheel rotates the rectangle (since we are not zooming the FOV in this wizard).
   - `RenderTransform` `RotateTransform(Rectangle.Rotation)`.
   - `Margin` via a `MultiBinding` to `Rectangle.X`, `Rectangle.Y`, and canvas size, mirroring the technique in `FramingAssistantView.xaml`.

**Pan handler in VM** (`DragMove`):
```
Rectangle.X += delta.X;
Rectangle.Y += delta.Y;
var imageArcsecWidth  = pixscale; // arcsec/pixel of the captured image
var imageArcsecHeight = pixscale;
// Captured image is centered in canvas; deltas in canvas pixels map 1:1 to image pixels at displayed scale.
var canvasPixelsPerImagePixel = displayedImageWidth / CapturedImageWidthPx;
var deltaImagePxX = (Rectangle.X - Rectangle.OriginalX) / canvasPixelsPerImagePixel;
var deltaImagePxY = (Rectangle.Y - Rectangle.OriginalY) / canvasPixelsPerImagePixel;
Rectangle.Coordinates = CapturedImageCoordinates.Shift(
    deltaImagePxX, deltaImagePxY,
    CapturedImageRotation, imageArcsecWidth, imageArcsecHeight);
```

Use `NINA.Astrometry.Coordinates.Shift(double deltaXpx, double deltaYpx, double rotationDeg, double scaleX, double scaleY)` — verified to be public and exactly the helper NINA's framing assistant uses for this conversion.

**Rotation handler in VM** (`RotateRectangle`):
```
var step = (mouseWheelDelta > 0) ? +1.0 : -1.0;
Rectangle.Rotation = AstroUtil.EuclidianModulus(Rectangle.Rotation + step, 360.0);
Rectangle.TotalRotation = AstroUtil.EuclidianModulus(CapturedImageRotation + Rectangle.Rotation, 360.0);
```

Also expose a Slider bound to `Rectangle.Rotation` (range −180…180) for fine adjustment, since wheel rotation alone is awkward.

**Recompute offsets on rectangle change:**
```
RAOffsetHours    = NormalizeRA(Rectangle.Coordinates.RA  - selectedObject.Coordinates.RA);
DecOffsetDegrees = Rectangle.Coordinates.Dec - selectedObject.Coordinates.Dec;
FinalPositionAngle = AstroUtil.EuclidianModulus(360 - Rectangle.TotalRotation, 360.0);
// Position-independent representation (display-only, see section 4.5):
OffsetSeparationArcsec = OrbitalOffsetMath.AngularSeparation(selectedObject.Coordinates, Rectangle.Coordinates);
OffsetPositionAngleDeg = OrbitalOffsetMath.PositionAngleNToE(selectedObject.Coordinates, Rectangle.Coordinates);
```
The base reference is `selectedObject.Coordinates` (current body position), NOT `CapturedImageCoordinates`, because the offset must be body-relative so the sequencer's `OrbitalsContainerBase.RefreshCoordinates()` can apply it on every refresh as the body moves.

### 4.5. Position-independent offset (display only, for now)

**Motivation.** The existing `OffsetCoordinates` on `OrbitalsContainerBase<T>` stores raw (ΔRA, ΔDec). RA hours, however, subtend `15·cos(Dec)` degrees on the sky, so a fixed (ΔRA, ΔDec) produces different on-sky displacements depending on the body's declination. A framing offset that "puts the comet head in the lower-left" at Dec=0 will not produce the same composition when the body climbs to Dec=+60° later in the apparition.

A position-independent offset captures the displacement in a frame that is invariant under translation along the sphere:
- **`OffsetSeparation`** — angular distance between body center and framing target, in arcseconds. This is the on-sky shift, period.
- **`OffsetPositionAngle`** — direction from body to framing target, measured from celestial north toward east, in degrees [0, 360). This is the standard astronomical position-angle convention and aligns with `Target.PositionAngle` for the rotator.

Applying `(separation, PA)` to any future body position yields the framing target via spherical trig:
```
δ₂ = asin(sin δ₁ · cos sep + cos δ₁ · sin sep · cos PA)
α₂ = α₁ + atan2(sin PA · sin sep · cos δ₁, cos sep − sin δ₁ · sin δ₂)
```
which is independent of where `(α₁, δ₁)` sits on the sphere (except at the pole, where PA becomes degenerate — handled explicitly).

**Scope for this plan.** Computed and displayed in the wizard read-only block alongside (ΔRA, ΔDec). **Not** wired into the sequencer container yet — a follow-up will add a new `OffsetMode { RaDec, SeparationPA }` to `OrbitalsContainerBase<T>` and consume these values at `RefreshCoordinates()` time. For now the export still uses (ΔRA, ΔDec); the new fields are informational so the user can record / copy / sanity-check them.

**Final rotation (`Target.PositionAngle`) is already position-independent** — it is the celestial-frame rotator angle, not an altaz angle, so no new variable is needed for rotation. The pair to add is just `(OffsetSeparation, OffsetPositionAngle)`.

**New helper class:** `NINA.Joko.Plugin.Orbitals/Calculations/OrbitalOffsetMath.cs` — pure static math, no DI, easy to unit-test:
- `double AngularSeparation(Coordinates c1, Coordinates c2)` — returns arcseconds. Use the haversine form for numerical stability at small separations:
  ```
  a = sin²(Δδ/2) + cos(δ₁)·cos(δ₂)·sin²(Δα/2)
  sep = 2·atan2(√a, √(1−a))
  ```
- `double PositionAngleNToE(Coordinates from, Coordinates to)` — returns degrees in `[0, 360)`. Uses `Math.Atan2` (not `Math.Atan` as in NINA's `AstroUtil.CalculatePositionAngle`, which has a quadrant ambiguity):
  ```
  y = sin(α₂ − α₁) · cos(δ₂)
  x = cos(δ₁)·sin(δ₂) − sin(δ₁)·cos(δ₂)·cos(α₂ − α₁)
  PA = (atan2(y, x) + 2π) mod 2π   // in degrees
  ```
- `Coordinates ApplyOffset(Coordinates from, double separationArcsec, double positionAngleDeg)` — inverse of the above pair, for the future container feature. Document that this is the function the sequencer will call once the container honors the new mode. Tests below exercise this round-trip even though no production caller exists yet — it keeps the helper honest for the follow-up.

### 5. Right-pane: orbital summary + altitude chart

- Reuse `NINA.WPF.Base.View.AltitudeChart` directly via the existing xmlns the plugin already uses:
  ```xml
  xmlns:alt="clr-namespace:NINA.WPF.Base.View;assembly=NINA.WPF.Base"
  ...
  <alt:AltitudeChart NighttimeData="{Binding NighttimeData}"
                     DataContext="{Binding SelectedObject}"
                     MinWidth="400" MinHeight="200" />
  ```
  Confirmed in `NINA.Joko.Plugin.Orbitals/View/OrbitalsView.xaml`.
- Summary block: `Name`, `CurrentRAString`, `CurrentDecString`, `RATrackingRate (sec/sidereal sec)`, `DecTrackingRate (arcsec/sec)`, `Distance (AU)`, `MaxExposureSeconds`. Read-only `TextBlock`s, all updating on the live ticker.
- Offsets block — two rows side by side:
  - **RA/Dec offset** (the values the sequencer will use today): `RA Offset` (HMS, signed), `Dec Offset` (DMS, signed), `Position Angle` (deg). Use the existing `HoursToHMSConverter` / `DegreesToDMSConverter` already wired up in the plugin's view resources.
  - **Position-independent offset** (display-only, per §4.5; labeled "Sky-frame offset (preview)" so the user knows it is informational): `Separation` (formatted as `arcsec` if <60, `arcmin` if <60, else `°`), `Offset PA` (deg, 0–360). Tooltip explains: "These values are independent of where the body is on the sky and will be honored by a future container variant."

### 6. Export to Advanced Sequencer

On `ExportToSequencerCommand`:

1. Resolve the matching container type for the selected object:

| `OrbitalsObjectBase` subtype | Container class |
|---|---|
| `OrbitalElementsObject` | `OrbitalObjectContainer` |
| `SolarSystemBodyObject` | `SolarSystemBodyContainer` |
| `TLEObject` | `ManualTLEContainer` |
| `PVTableObject` | `JWSTContainer` |

2. Call `sequenceMediator.GetDeepSkyObjectContainerTemplates()` and find the template with the matching runtime type. Fallback: if no template is present, instantiate a fresh one via MEF (each `OrbitalsContainerBase<T>` subclass already has a parameterless-ish MEF constructor used by the sequence template provider).

3. `Clone()` the template (NINA's `IDeepSkyObjectContainer.Clone` returns a typed `ISequenceContainer`; cast back to the concrete orbital container type).

4. Populate the clone:
   - Container-specific selection — set what the deserialized container would have set, e.g.:
     - `OrbitalObjectContainer`: `ObjectType = ((OrbitalElementsObject)selectedObject).ObjectType`, `SelectedOrbitalName = selectedObject.Name` (this triggers its `OrbitalSearchVM` to resolve the underlying elements).
     - `SolarSystemBodyContainer`: `SelectedSolarSystemBody = ((SolarSystemBodyObject)selectedObject).SolarSystemBody`.
     - `ManualTLEContainer`: write the TLE lines/name (verify the exact JsonProperty names when implementing).
     - `JWSTContainer`: name only (PV table source is its sole resolver path).
   - `Target.TargetName = selectedObject.Name`.
   - `Target.PositionAngle = FinalPositionAngle`.
   - `OffsetCoordinates.Coordinates = new Coordinates(Angle.ByHours(RAOffsetHours), Angle.ByDegree(DecOffsetDegrees), Epoch.J2000)`.
   - **Note:** the position-independent `(OffsetSeparation, OffsetPositionAngle)` values from §4.5 are **not** written to the container in this plan — the container does not yet support that mode. They are computed and shown in the wizard so the user can record them; a follow-up will add the consumer side.
5. `sequenceMediator.AddAdvancedTarget(container);` then `applicationMediator.ChangeTab(ApplicationTab.SEQUENCE);` and close the wizard window.

### 7. Hooking the existing Frame button

Modify `OrbitalsVM.SendToFramingWizardCommandAction()` (currently at lines ~176–197):

```csharp
private async Task<bool> SendToFramingWizardCommandAction() {
    if (SelectedOrbitalsObject == null) return false;
    var wizardVm = wizardFactory();                       // injected Func<OrbitalFramingWizardVM>
    wizardVm.Initialize(SelectedOrbitalsObject);
    var windowService = windowServiceFactory.Create();
    await windowService.ShowDialog(
        wizardVm,
        title: Loc.Instance["LblOrbitalFramingWizard"],   // add to plugin locale
        resizeMode: ResizeMode.CanResize,
        style: WindowStyle.SingleBorderWindow);
    return true;
}
```

Inject `Func<OrbitalFramingWizardVM>` and `IWindowServiceFactory` into `OrbitalsVM`'s `[ImportingConstructor]`. Remove the old `framingAssistantVM.SetCoordinates(dso)` path and the `applicationMediator.ChangeTab(FRAMINGASSISTANT)` call from this command (`IFramingAssistantVM` injection can remain — it's used elsewhere or can be removed in a follow-up).

### 8. Critical files

**New (production):**
- `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs`
- `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml(.cs)` — the `Window`.
- `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml(.cs)` — the layered image+rectangle `UserControl`.
- `NINA.Joko.Plugin.Orbitals/Calculations/OrbitalOffsetMath.cs` — separation / position-angle helpers (§4.5).
- `NINA.Joko.Plugin.Orbitals/Imaging/ICaptureSource.cs` — abstraction (§3.1) plus `CapturedFrame` and `ICaptureSourceMetadata` (Phase A).
- `NINA.Joko.Plugin.Orbitals/Imaging/XisfStubCaptureSource.cs` — static-XISF source (Phase B).
- `NINA.Joko.Plugin.Orbitals/Imaging/LiveCaptureSource.cs` — live capture source (Phase E).

**Modified:**
- `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalsVM.cs` — replace `SendToFramingWizardCommandAction` body; inject wizard factory + window-service factory.
- `NINA.Joko.Plugin.Orbitals/Properties/OrbitalsOptions.cs` (or wherever options live) — add `CaptureMode` enum (`XisfStub` (Phase A default), `Live`) and persisted setting.
- Locale resources file (if one exists; otherwise embed strings literally in XAML for now and add localization in a follow-up).

**Reused — no edits required:**
- `OrbitalsObjectBase` and concrete subtypes for live position/tracking-rate/altitude data.
- `OrbitalsContainerBase<T>` subclasses for the sequencer export targets.
- `NINA.Astrometry.Coordinates.Shift`, `NINA.Astrometry.AstroUtil.EuclidianModulus`, `NINA.Astrometry.FramingRectangle`.
- `NINA.WPF.Base.View.AltitudeChart`, `DragCommandBehavior`, `MouseWheelCommandBehavior`, brush resources (`ButtonForegroundBrush`, `SecondaryBackgroundBrush`, etc.).
- `IPlateSolverFactory.GetCenteringSolver`, `ICenteringSolver.Center`, `ISkySurveyFactory`, `ISequenceMediator.AddAdvancedTarget`, `INighttimeCalculator`.

### 9. Test suite (additions to `NINA.Joko.Plugin.Orbitals.Tests`)

The repo already has the test project (`NINA.Joko.Plugin.Orbitals.Tests/`) with the `Fixtures.cs` provenance pattern (real orbital element sets cited from JPL/MPC) and folders for `Calculations/`, `ViewModels/`, etc. Follow the established conventions — citation comments on every fixture, no inlined magic numbers, mock NINA mediators with the same library the existing tests use (inspect `OrbitalSearchVMTests.cs` and `SetGuiderShiftRateTests.cs` to match — likely `NSubstitute` or `Moq`).

**Tolerances.** Use:
- `1e-9` for normalized round-trips of pure spherical trig (radians).
- `1e-4 arcsec` for separation round-trips after converting via the helper.
- `1e-3 deg` for position-angle round-trips away from singular regions.
- Looser tolerances (`1e-2 arcsec`) near the celestial pole, where PA is degenerate.

#### 9.1 `Calculations/OrbitalOffsetMathTests.cs`

Pure math against `OrbitalOffsetMath`.

- **Separation symmetry:** `AngularSeparation(a, b) == AngularSeparation(b, a)` for a grid of `(α, δ)` pairs covering equator, mid-latitude, and high-Dec.
- **Separation against a known table:** hard-coded reference pairs computed independently (e.g., `(0h, 0°) → (1h, 0°)` is exactly 15°; `(0h, 60°) → (1h, 60°)` is `acos(sin²60 + cos²60·cos15)` ≈ 7.476°). Numbers cited in comments with the formula they came from.
- **Position-angle quadrants:** PA from `(0h, 0°)` to `(0h, +1°)` ≈ 0° (north); to `(+1m, 0°)` ≈ 90° (east); to `(0h, −1°)` ≈ 180°; to `(−1m, 0°)` ≈ 270°. Plus a 45° diagonal check.
- **PA matches `AstroUtil.CalculatePositionAngle` away from singularities** (modulo NINA's `Atan` quadrant issue — wrap the NINA result into `[0, 360)` for the comparison, and skip the comparison when our PA is in the southern half where `Atan` will have collapsed).
- **Round-trip — same body, varying PA:** for a fixed body coord and a sweep of `(sep ∈ {1″, 1′, 10′, 1°, 30°}, PA ∈ 0..360° step 30°)`, compute `b = ApplyOffset(a, sep, PA)`, then `(sep', PA') = (AngularSeparation(a, b), PositionAngleNToE(a, b))`. Assert `sep' ≈ sep` and `PA' ≈ PA` mod 360.
- **Pole singularity:** at `Dec=+89.99°` confirm `ApplyOffset` produces a finite result and that the round-trip separation matches; document that PA exactly at the pole is intentionally returned as `0`.

#### 9.2 `Calculations/OrbitalOffsetEquivalenceTests.cs` — *position-independence demonstration*

This is the headline test for the new offset variables, per the user's explicit ask: *"Make additional cases for the new adjustment method that demonstrates it having the same image offset at an entirely different set of RA/Dec coordinates."*

- For each of these distinct anchor coordinates (chosen to span declination):
  - `A = (0h, 0°)`
  - `B = (6h, +30°)`
  - `C = (18h, +60°)`
  - `D = (12h, −20°)`
  
  And for each of these on-sky framing intents (separation, PA):
  - `(10′, 45°)` — small, NE
  - `(30′, 270°)` — moderate, due west
  - `(1.5°, 135°)` — larger, SE
  
  Confirm:
  1. `ApplyOffset(anchor, sep, PA)` produces a result whose `AngularSeparation` back to `anchor` equals `sep` (proves the new pair gives the same on-sky displacement regardless of where on the sky we are).
  2. The equivalent RA/Dec offsets `(ΔRA, ΔDec)` computed at each anchor are **different in magnitude** between anchors (proves the old representation is position-dependent — the bug the new representation fixes). Specifically assert `|ΔRA_A − ΔRA_C| > some threshold` for the same `(sep, PA)`.
  3. The "image-frame offset" (gnomonic tangent-plane projection of the offset around the anchor; one helper invocation each) matches across anchors within tolerance, sealing the equivalence claim.

#### 9.3 `Calculations/OrbitalFramingScenarios.cs` — real-target fixtures

Extend the existing `Fixtures.cs` pattern. Citation-commented entries returning `Coordinates` and a brief context blurb:
- **Comet C/2023 A3 Tsuchinshan–ATLAS** at 2024-10-26 22:00 UTC (post-perihelion, low Dec; matches the timestamp in the user's stub XISF) — coords from JPL Horizons.
- **Comet 1P/Halley** at JD 2449400.5 (already in `Fixtures.Halley()`) — compute current geocentric `Coordinates` via `OrbitalElementsAccessor.GetObjectPV(...)` and freeze the result as a literal in the fixture so the test does not re-run Kepler each invocation.
- **Asteroid 1 Ceres** at JD 2460200.5 (already in `Fixtures.Ceres()`).
- **Planet Mars** at 2025-06-15 04:00 UTC (a mid-Dec, mid-RA case) — coords from JPL Horizons.
- **Planet Jupiter** at 2026-01-15 02:00 UTC (high Dec).

Each fixture exposes both the J2000 coordinates and an expected current `SiderealShiftTrackingRate` snapshot, sourced and cited the same way.

#### 9.4 `ViewModels/OrbitalFramingWizardVMTests.cs`

Drive the VM with a fake `ICaptureSource` (`FakeCaptureSource : ICaptureSource` defined in `TestHelpers/`) that returns a deterministic `CapturedFrame` (synthetic 1×1 `BitmapSource`, known coordinates, known rotation, known pixscale). Inject a mocked `ISkySurveyFactory` that returns a stub `SkySurveyImage`.

For each `OrbitalFramingScenarios` fixture (comets, asteroids, planets):

- **Pan offset math:** with a fixed pixel pan `(dx, dy)`, assert `RAOffsetHours`/`DecOffsetDegrees` match the analytic prediction (use `Coordinates.Shift` directly to compute the expected target, then differ against body coords).
- **Offset position-angle math:** the same pan, compared against the analytic `AngularSeparation` / `PositionAngleNToE` on the produced rectangle coords.
- **Rotation math:** after rotating the rectangle by `θ`, assert `FinalPositionAngle == (360 − (CapturedImageRotation + θ)) mod 360`.
- **Reset framing:** all four offset readouts return to zero.

#### 9.5 `ViewModels/OrbitalFramingWizardExportTests.cs`

Mock `ISequenceMediator.GetDeepSkyObjectContainerTemplates()` to return one of each orbital container template (`OrbitalObjectContainer`, `SolarSystemBodyContainer`, `ManualTLEContainer`, `JWSTContainer`). Mock `AddAdvancedTarget` to capture the container.

For each `OrbitalsObjectBase` subtype:
- `Export` selects the **correct** template class.
- The clone receives:
  - `Target.TargetName == selectedObject.Name`.
  - `Target.PositionAngle == FinalPositionAngle` (within `1e-6`).
  - `OffsetCoordinates.Coordinates.RA == RAOffsetHours` and `.Dec == DecOffsetDegrees` (within `1e-9`).
  - The container-specific selector (`ObjectType`, `SelectedSolarSystemBody`, etc.) is populated per the §6 table.
- `AddAdvancedTarget` was called exactly once.
- `IApplicationMediator.ChangeTab(SEQUENCE)` was called.

#### 9.6 `Calculations/OrbitalFramingOffsetRealCasesTests.cs` — *both methods, real targets*

Per the user's explicit ask: *"Confirm both adjustment methods (RA/Dec offset and the new one) work."* The two parameterized scenarios:

- **Scenario A — body-relative shift, same target:** for each real fixture (comet C/2023 A3, asteroid 1 Ceres, Mars, Jupiter), apply a `(ΔRA, ΔDec)` of (e.g.) `(+30″/cos(Dec), +60″)` and verify the resulting coordinates land where expected. This is the existing offset method; tests confirm `OrbitalsContainerBase.RefreshCoordinates()`-equivalent math (the addition-then-modulus dance in §RefreshCoordinates) yields the expected on-sky position. Implemented by directly invoking that math path; no live container instantiation needed.
- **Scenario B — invariant (sep, PA), same image offset across targets:** pick `(sep=20′, PA=60°)`. Apply it to each fixture's anchor via `OrbitalOffsetMath.ApplyOffset`. Assert:
  - `AngularSeparation(anchor, result) == 20′` (within `1e-4 arcsec`) for every fixture.
  - The local east/north tangent-plane projection of `(anchor → result)` (computed via `Coordinates.Shift`'s inverse) matches between fixtures within `1e-3 arcsec` — the same image-frame offset shows up at C/2023 A3's coords, at Ceres's coords, and at Jupiter's coords.
  - The `(ΔRA, ΔDec)` representation of the same intent differs measurably between fixtures (assertion ≥ 1″ between min and max `ΔRA` across the four anchors) — empirical confirmation that the old representation drifts and the new one does not.

#### 9.7 Test helpers (`TestHelpers/FakeCaptureSource.cs`, `TestHelpers/FakeSkySurveyFactory.cs`)

Tiny in-test doubles. The `FakeCaptureSource` exposes a `CapturedFrame Next { get; set; }` so each test can stage what the wizard "captures". `FakeSkySurveyFactory` returns a `SkySurveyImage` with a 1×1 `BitmapSource` and the requested coordinates/FOV/rotation; the wizard does not inspect bitmap content.

#### 9.8 CI

`tests.yml` already runs the test project per recent commit `0c8fabb`. No workflow changes required — the new tests run automatically. Confirm `coverlet.runsettings` does not exclude the new namespaces (`Calculations`, `ViewModels`, `Imaging`); add includes if needed.

### 10. Verification

Verification is staged per phase. Each phase's implementation commit must pass its own checks before the code-review-and-fix commit; the code-review-and-fix commit re-runs the same checks.

**Build (every phase).** `dotnet build NINA.Joko.Plugin.Orbitals/NINA.Joko.Plugin.Orbitals.csproj -c Debug` succeeds, `TestApp/TestApp.csproj` still builds, and the post-build target copies the DLL to `%localappdata%\NINA\Plugins\3.0.0\Orbitals`.

**Tests (every phase that ships new code).** `dotnet test NINA.Joko.Plugin.Orbitals.Tests/NINA.Joko.Plugin.Orbitals.Tests.csproj` is green. Coverage of new files reported via `coverlet.runsettings` does not regress overall numbers.

**Phase A — math + abstractions.**
- All §§9.1–9.3 and §9.6 tests pass. No production caller exists yet for `OrbitalOffsetMath`; tests exercise it directly. `ICaptureSource` compiles with no implementations.

**Phase B — VM + XISF stub.**
- `dotnet test` passes with §9.4 added.
- Manual smoke in NINA: search a comet in the Orbitals dock → click Frame → window opens with summary populated and "Load Test Image" button enabled. Clicking it loads the configured XISF, captured-image state populates, status bar shows progress. No equipment activity is observed (no slew, no capture, no plate-solve calls). No XAML layout judgement at this phase — placeholder UI is fine.

**Phase C — UI: canvas + altitude chart + offsets display.**
- Manual smoke in NINA: open wizard → Load Test Image → captured image renders on top of a sky-survey background in the center of the canvas (3× FOV). Drag the rectangle → both `RA Offset / Dec Offset` and the new `Separation / Offset PA` readouts update live and remain self-consistent (re-compute one from the other by hand for one drag position; values match within display precision). Rotate via wheel and via the slider → `Position Angle` updates; rectangle visibly rotates. Reset Framing → all four readouts return to 0. Altitude chart populates with the body's curve and nighttime shading.
- Repeat for one comet, one asteroid, one planet — confirm summary block updates correctly per type.

**Phase D — sequencer export.**
- `dotnet test` passes with §9.5 added.
- Manual smoke in NINA: pan + rotate against the static XISF, then Export to Sequencer → wizard closes, NINA navigates to the Sequencer tab, a container of the correct subtype is present with the displayed offsets and position angle pre-populated. Verify by opening the container UI that `OffsetCoordinates` and `Target.PositionAngle` match the wizard's last readout.
- Cross-type coverage: repeat the export step for each `OrbitalsObjectBase` subtype (orbital-elements comet, orbital-elements asteroid, solar-system body, manual TLE, JWST/PV-table) and confirm the correct container subclass is added each time.
- **End-to-end against the static XISF is the gating criterion for starting Phase E.**

**Phase E — `LiveCaptureSource`.**
- `OrbitalsOptions.CaptureMode` toggles between `XisfStub` and `Live`; persisting and reloading the profile preserves the choice.
- Manual smoke in NINA with the **camera + mount simulators** (no real gear required for this verification): set `CaptureMode = Live`, open the wizard, click "Slew, Center & Image". Observe `ICenteringSolver` progress notifications; the captured image renders centered on the survey background; the framing rectangle appears overlaid. Drag, rotate, Reset Framing, Export to Sequencer — all the Phase C/D interactions work identically against the live-captured image.
- Cancellation: cancel mid-capture — the solver tears down cleanly and the wizard returns to its pre-capture state without locking up the camera mediator.
- Toggle back to `XisfStub` and confirm the entire Phase B–D flow still works (no regression).

**No-regression check (every phase).** The existing Orbitals dock continues to work; the only behavior change visible to the user is what the Frame button does.
