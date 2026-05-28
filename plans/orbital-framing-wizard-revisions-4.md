# Orbital Framing Wizard — Revisions Round 4

## Context

The framing wizard has shipped through three rounds of revisions. Recent commits
fixed the MEF construction of `SkySurveyFactory`, added the three-layer canvas
(sky-survey / captured image / framing rectangle), and polished zoom/auto-stretch
behaviour. Four refinements remain before the wizard is ready for general use:

1. The separation between target and reference is displayed in arcseconds only,
   so values north of about an arcminute are hard to read at a glance.
2. The offline sky-map still does not render behind the captured frame in
   practice, even though the fetch is wired up.
3. Once the sky-map renders, the captured image needs to be partially
   transparent so it doesn't mask the chart underneath.
4. The user-facing offset inputs in the four sequencer containers are RA hours
   plus Dec degrees, which scale with declination and confuse users. Real
   reliable inputs are *angular separation* and *position angle*; RA/Dec
   offsets should be derived (with a modal for entering them directly when
   that's more convenient).

## Affected files

| File | Why it changes |
|---|---|
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml` | Separation display now shows d°m′s″. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingCanvas.xaml` | Captured-image `Opacity` 0.85 → 0.80. |
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs` | New `OffsetSeparationDisplay` derived property; default sky-survey source uses profile preference; expanded diagnostic logging; HIPS2FITS fallback. |
| `NINA.Joko.Plugin.Orbitals/Calculations/OrbitalOffsetMath.cs` | Reuse as-is — already has `AngularSeparation`, `PositionAngleNToE`, `ApplyOffset`. |
| `NINA.Joko.Plugin.Orbitals/SequenceItems/OrbitalsContainerBase.cs` | Add canonical `OffsetSeparationArcsec` + `OffsetPositionAngleDeg` JSON properties. `RefreshCoordinates` switches to `OrbitalOffsetMath.ApplyOffset`. `[OnDeserialized]` migrates legacy RA/Dec offsets. `OffsetCoordinates` becomes a runtime-only view of the derived RA/Dec offset. |
| `NINA.Joko.Plugin.Orbitals/SequenceItems/DataTemplates.xaml` | Replace the "Offset (Advanced)" panel in all four container templates with Separation + Offset PA inputs, derived RA/Dec read-only display, and an "Enter RA/Dec…" button. The four templates share the same shape — define the new offset block once as a `DataTemplate` (or `Style`/`ContentControl` shared resource) and reuse it. |
| `NINA.Joko.Plugin.Orbitals/View/RADecOffsetDialog.xaml` *(new)* | Small `Window` for RA hours / Dec degrees input with OK/Cancel. |
| `NINA.Joko.Plugin.Orbitals/View/RADecOffsetDialog.xaml.cs` *(new)* | Code-behind hosting RA/Dec input controls; commits derived Separation + PA to the parent container on OK. |

## Detailed approach

### 1 — Separation display in degrees / arcminutes / arcseconds

Add a derived property to `OrbitalFramingWizardVM`:

```csharp
public string OffsetSeparationDisplay {
    get {
        var totalArcsec = Math.Abs(offsetSeparationArcsec);
        var deg = (int)(totalArcsec / 3600.0);
        var arcmin = (int)((totalArcsec / 60.0) % 60.0);
        var arcsec = totalArcsec - deg * 3600.0 - arcmin * 60.0;
        return $"{deg:D2}° {arcmin:D2}′ {arcsec:F1}″";
    }
}
```

Raise `PropertyChanged(nameof(OffsetSeparationDisplay))` everywhere
`OffsetSeparationArcsec` is set (only the setter at line 813). Update
`OrbitalFramingWizardView.xaml` lines 328–332 to bind `OffsetSeparationDisplay`
instead of `OffsetSeparationArcsec` with a `StringFormat`.

### 2 — Sky-map not rendering

Two changes to `OrbitalFramingWizardVM.LoadInitialImageSource()` and
`ReloadBackgroundAsync()`:

a. Drop the hardcoded `SkySurveySource.SKYATLAS`. Read
   `profileService.ActiveProfile.FramingAssistantSettings.LastSelectedImageSource`
   first; if unset or invalid, default to `SkySurveySource.HIPS2FITS`.

b. Add per-step diagnostic logging inside `ReloadBackgroundAsync` (lines 314–330):
   one log line *before* `GetImage`, one *after* with the image dimensions or
   `null`, and one *after* assigning `BackgroundImage`. This makes NINA.log
   diagnose-able for whoever reports next.

c. If `GetImage` returns null or throws and the current source is not
   `HIPS2FITS`, try once more with `HIPS2FITS` as a fallback before giving up.
   Log clearly that the fallback was used.

### 3 — Captured-image opacity → 0.80

Single-character edit in `OrbitalFramingCanvas.xaml` line 33:
`Opacity="0.85"` → `Opacity="0.80"`.

### 4 — Separation + Offset PA as primary sequencer inputs

#### Model (`OrbitalsContainerBase.cs`)

Add two new persisted properties:

```csharp
[JsonProperty] public double OffsetSeparationArcsec { get; set; }
[JsonProperty] public double OffsetPositionAngleDeg { get; set; }
```

Each setter raises `PropertyChanged` and calls `RaiseOffsetChanged()`.

`RefreshCoordinates()` switches from additive RA/Dec math (lines 135–146) to:

```csharp
var newCoords = OrbitalOffsetMath.ApplyOffset(
    targetCoordinates,
    OffsetSeparationArcsec,
    OffsetPositionAngleDeg);
targetCoordinates.RA  = newCoords.RA;
targetCoordinates.Dec = newCoords.Dec;
```

Wrap that in the existing pole-clamp guard.

Keep `OffsetCoordinates` (`InputCoordinatesEx`) as a runtime-only **derived
view** of the RA/Dec offset for the *current* target position. Recompute it
inside `RefreshCoordinates()` after the new coords are applied:

```csharp
OffsetCoordinates.Coordinates = new Coordinates(
    Angle.ByHours(newCoords.RA - originalRA),
    Angle.ByDegree(newCoords.Dec - originalDec),
    Epoch.J2000);
```

This gives the existing DataTemplate bindings (`RAHours`, `DecDegrees`, …)
read-only values to display.

**Migration for old saves**: in the existing `[OnDeserialized]` handler at
line 96, if `OffsetSeparationArcsec == 0 && OffsetPositionAngleDeg == 0` but
`OffsetCoordinates` has nonzero values, compute the equivalent Separation+PA
once the first `RefreshCoordinates` runs (it needs `TargetObject` available).
The cleanest place is the first invocation of `RefreshCoordinates()` itself,
guarded by a `_legacyMigrationPending` flag.

#### UI (`DataTemplates.xaml`)

The four "Offset (Advanced)" panels (OrbitalObjectContainer ≈ lines 272–413,
SolarSystemBodyContainer ≈ 598–739, JWSTContainer ≈ 931–1072,
ManualTLEContainer ≈ 942–1071) all use the same shape. Extract the new offset
block once as a shared resource:

```xaml
<DataTemplate x:Key="OrbitalOffsetBlock">
    <!-- Editable: Separation (arcsec) -->
    <!-- Editable: Offset PA (degrees) -->
    <!-- Read-only: RA Offset, Dec Offset (bound to OffsetCoordinates.RAHours/DecDegrees) -->
    <!-- Button: "Enter RA/Dec…" -->
</DataTemplate>
```

Each container's template references it via `<ContentControl
ContentTemplate="{StaticResource OrbitalOffsetBlock}" />`. The button's
`Click` handler is in code-behind (`DataTemplates.xaml.cs` already exists)
and opens `RADecOffsetDialog`.

#### Modal (`RADecOffsetDialog.xaml` / `.cs`)

A small `Window` styled to match NINA's other dialogs:

- Two NINA-style RA/Dec input groups (re-use the `MultiBinding +
  DecDegreeConverter` pattern from the existing offset XAML; bind them
  against a local `InputCoordinatesEx` populated from the container's
  current Sep+PA-derived offset).
- OK and Cancel buttons.

`.xaml.cs` exposes the entered `Coordinates` on `DialogResult==true`. The
button click in `DataTemplates.xaml.cs`:

1. Looks up the container from the button's `DataContext`.
2. Computes the current RA/Dec offset (from `OffsetCoordinates`) and seeds
   the dialog.
3. Calls `ShowDialog()`.
4. On OK: converts the entered RA/Dec offset → Separation+PA via
   `OrbitalOffsetMath.AngularSeparation` / `PositionAngleNToE` (using the
   container's current `TargetCoordinates` as the reference), and writes
   the result to `container.OffsetSeparationArcsec` /
   `container.OffsetPositionAngleDeg`.

#### Persistence interaction with InputCoordinatesEx

Drop `[JsonProperty]` from `OffsetCoordinates` so existing-but-old saves
that had `OffsetCoordinates` populated still load the field (Newtonsoft
deserializes by name regardless), but newly-saved files only carry
Sep+PA. The migration step in `RefreshCoordinates` will populate Sep+PA
from a legacy `OffsetCoordinates` on first refresh.

## Verification

1. **Build the plugin**: `dotnet build NINA.Joko.Plugin.Orbitals.sln` (Release).
   No compile errors.
2. **TestApp end-to-end (item 2)**: launch `TestApp/TestApp.csproj`, open the
   wizard against an orbital target, watch NINA.log for the new fetch logs.
   Confirm the sky-survey image appears under the captured frame. Confirm the
   captured frame is visibly translucent. If the fetch falls back to HIPS2FITS,
   the log line tells you so.
3. **Wizard separation display (item 1)**: capture a frame, drag the framing
   rectangle, confirm the Separation field reads e.g. `00° 02′ 14.6″` and
   updates live as the rectangle moves.
4. **Sequencer item migration (item 4)**:
   a. Load a sequence saved with a previous build that has nonzero RA/Dec
      offsets. After first refresh, the Sep+PA fields should be populated and
      the framing should be identical to before (compare current coordinates).
   b. With a fresh container, enter a Separation and PA. Verify the displayed
      RA/Dec offsets match `OrbitalOffsetMath.ApplyOffset` math when computed
      manually with the container's current coordinates.
   c. Click "Enter RA/Dec…", enter a RA/Dec pair, hit OK. Confirm Sep+PA
      update to the equivalent values and the read-only RA/Dec display
      matches what was entered.
   d. Re-save the sequence, reload it, confirm Sep+PA round-trip.
5. **Sanity in OrbitalsView**: confirm the top-level OrbitalsView Set
   Offset / Reset Offset commands still work and the read-only RA/Dec
   display is unchanged (this view is intentionally out of scope).
