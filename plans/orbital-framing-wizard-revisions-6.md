# Orbital Framing Wizard — Revisions Round 6

## Context

Two follow-up issues:

1. **Sky-map is just a flat bitmap.** NINA's framing assistant overlays an
   RA/Dec grid and labels for named sky objects (DSOs, constellations) on top
   of the survey image. Our wizard just shows the raw survey bitmap, so the
   user has no visual reference for what's where on the sky. NINA already
   ships a reusable annotator (`SkyMapAnnotator` in `NINA.WPF.Base.SkySurvey`)
   that produces this overlay; we can use it directly instead of re-implementing.

2. **Zoom UX flaws.**
   - The "100%" label between the +/− buttons is a static string. It doesn't
     update as the user zooms with Ctrl+wheel or the buttons.
   - The minimum zoom is 0.25 (zoom out below the starting point). The user
     wants 1.0 as the floor.

## Affected files

| File | Why it changes |
|---|---|
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs` | Owns a `SkyMapAnnotator`; initializes it after each survey fetch; exposes `SkyMapOverlay`. `MinZoom` constant changes from `0.25` → `1.0`. |
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalsVM.cs` | Pass `telescopeMediator` (already an injected dependency on `OrbitalsVM`) through to the wizard VM's constructor. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml` and `.xaml.cs` | New `AnnotationLayer` `Image` between `BackgroundLayer` and `CapturedLayer`. New `AnnotationImageSource` DependencyProperty. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml` | Bind new `AnnotationImageSource="{Binding SkyMapOverlay}"` on the canvas. Replace the static "100%" `TextBlock` with `Text="{Binding CanvasZoom, StringFormat={}{0:P0}}"`. |

## Detailed approach

### 1. Reuse NINA's `SkyMapAnnotator`

The annotator (`/mnt/c/Users/ghili/src/nina/NINA.WPF.Base/SkySurvey/SkyMapAnnotator.cs`)
is public and self-contained. Key API:

```csharp
public SkyMapAnnotator(ITelescopeMediator mediator, IProfileService profileService);
public async Task Initialize(Coordinates centerCoordinates, double vFoVDegrees,
    double imageWidth, double imageHeight, double imageRotation,
    CacheSkySurvey cache, CancellationToken ct);
public BitmapSource SkyMapOverlay { get; }   // observable, raised by UpdateSkyMap()
public void UpdateSkyMap();
```

The `ITelescopeMediator` parameter is null-safe (lines 75, 135 of
`SkyMapAnnotator.cs` use `?.`). Passing the mediator gets us a "telescope is
pointing here" marker — nice to have, and `OrbitalsVM` already has the
mediator imported, so plumb it through:

- Add `ITelescopeMediator telescopeMediator` to `OrbitalFramingWizardVM`'s
  constructor (the wizard is constructed by `OrbitalsVM`, not by MEF, so this
  is a one-call-site change).

Add the annotator to the wizard VM:

```csharp
private readonly SkyMapAnnotator skyMapAnnotator;

// in constructor:
skyMapAnnotator = new SkyMapAnnotator(telescopeMediator, profileService);
skyMapAnnotator.PropertyChanged += (_, e) => {
    if (e.PropertyName == nameof(skyMapAnnotator.SkyMapOverlay)) {
        RaisePropertyChanged(nameof(SkyMapOverlay));
    }
};

public BitmapSource SkyMapOverlay => skyMapAnnotator?.SkyMapOverlay;
```

Update `ReloadBackgroundAsync` (after `BackgroundImage = bmp;` succeeds) to
initialize the annotator with the same center coordinates, FOV, and image
dimensions that the survey was fetched at. The annotator API wants **vertical
FOV in degrees**; our existing `fovArcmin` divides by 60 to convert:

```csharp
await skyMapAnnotator.Initialize(
    coords,
    fovArcmin / 60.0,
    BackgroundImagePx,
    BackgroundImagePx,
    0.0,            // rotation: survey images are north-up
    null,           // cache: not needed for our use
    ct);
```

`Dispose()` should null-check and call `telescopeMediator.RemoveConsumer(...)`
or just let GC handle it (the annotator does that on its next Initialize).

### 2. New `AnnotationLayer` in the canvas

`OrbitalFramingCanvas.xaml` — add the layer between Background and Captured:

```xaml
<!-- Layer 1: Background survey image -->
<Image x:Name="BackgroundLayer" ... />

<!-- Layer 1.5: Sky-map annotations (RA/Dec grid + DSO labels) -->
<Image x:Name="AnnotationLayer"
       Stretch="Uniform"
       HorizontalAlignment="Stretch"
       VerticalAlignment="Stretch"
       IsHitTestVisible="False" />

<!-- Layer 2: Captured image ... -->
```

Add a `DependencyProperty AnnotationImageSourceProperty` to
`OrbitalFramingCanvas.xaml.cs`, mirroring the existing
`BackgroundImageSourceProperty` exactly (with an `OnAnnotationImageSourceChanged`
callback that assigns to `AnnotationLayer.Source`).

In `OrbitalFramingWizardView.xaml`, bind:

```xaml
<local:OrbitalFramingCanvas
    ...
    BackgroundImageSource="{Binding BackgroundImage}"
    AnnotationImageSource="{Binding SkyMapOverlay}"
    ... />
```

Both bitmaps cover the same Stretch=Uniform parent → they're aligned because
the annotator was initialized with the same center/FOV/dimensions as the
survey fetch.

### 3. Zoom percentage display

Single-line XAML change in `OrbitalFramingWizardView.xaml` line 108. Replace
the static label:

```xaml
<TextBlock Text="100%" FontSize="10" Foreground="..." />
```

with a live binding:

```xaml
<TextBlock Text="{Binding CanvasZoom, StringFormat={}{0:P0}}"
           FontSize="10"
           Foreground="..." />
```

`P0` is a percent format with zero decimals → `1.0 → "100 %"`, `1.5625 → "156 %"`.
(WPF inserts a thin non-breaking space; if that's visually objectionable, use
`StringFormat={}{0:0%}` instead, which produces `"100%"` without space.)

`RaisePropertyChanged()` on `CanvasZoom` already fires whenever the setter
runs, and that's what re-evaluates the binding. No VM change needed.

### 4. Minimum zoom = 100%

In `OrbitalFramingWizardVM.cs` line 116:

```csharp
public const double MinZoom = 1.0;   // was 0.25
```

The existing setter clamps on every assignment (line 134), so the change
propagates automatically. Wheel-out below 1.0 just no-ops.

(Optional polish, not in this plan: disable the Zoom Out button when
`CanvasZoom <= MinZoom`. Skipping — the no-op behavior is clear enough.)

## Verification

1. **Build clean:** `dotnet build NINA.Joko.Plugin.Orbitals.sln -c Release`.
2. **Annotations show up:** Open the wizard against an orbital target. The
   background survey image should now have:
   - Blue/grey RA and Dec grid lines across the image
   - Labels for major DSOs that fall in the visible FOV
   - Constellation lines/boundaries (whatever NINA's annotator draws)
3. **Annotations move with the survey:** Capture a frame. The wizard re-fetches
   the survey at the plate-solved coordinates with the real FOV. The annotation
   overlay should re-render to match — grid lines should still align with the
   image content.
4. **Zoom percent updates:** Press Ctrl+wheel up — the label between the +/−
   buttons updates from `100%` to `125%`, `156%`, etc.
5. **Zoom floor:** With the wizard at `100%`, Ctrl+wheel down or click the
   `−` button — the value stays at `100%` and the canvas does not zoom out.
