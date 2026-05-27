# Orbital Framing Wizard — Revisions Round 5

## Context

The wizard's "Capture" section currently has plain TextBoxes for **Gain** and
**Offset** with hardcoded defaults of `0`, and the value typed there isn't
actually used for the framing capture (`LiveCaptureSource` reads
`plateSolveSettings.Gain` for the centering loop and ignores both `Gain` and
`Offset` for the final framing exposure). NINA's built-in snapshot dock
(`AnchorableSnapshotVM` / `AnchorableCameraControlView`) handles this nicely:
the input blanks-out when the value is `-1`, which the camera driver layer
interprets as "fall back to the camera-settings default." We want the same
behavior for the wizard so the user doesn't have to type a value when they're
fine with their configured default.

A useful side effect: the new wiring also fixes the existing bug where the
wizard's Gain/Offset inputs had no effect on the captured frame.

## Affected files

| File | Why it changes |
|---|---|
| `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs` (lines 698–708) | Change `Gain` and `Offset` defaults from `0` to `-1`. |
| `NINA.Joko.Plugin.Orbitals/View/OrbitalFramingWizardView.xaml` (Capture group ~lines 222–228) | Bind Gain/Offset TextBoxes via NINA's `MinusOneToEmptyStringConverter` (already in NINA's converter resources, which the wizard already merges in). Optional: add the same `IntRangeRuleWithDefault` validation NINA's snapshot view uses. |
| `NINA.Joko.Plugin.Orbitals/Imaging/LiveCaptureSource.cs` (lines ~157–166) | Pass `exposure.Gain` and `exposure.Offset` onto the framing `CaptureSequence` so the value the user types (or leaves blank) actually flows to the camera. |

`OrbitalFramingExposureSettings` in `ICaptureSource.cs` (lines 14–19) already
exposes `Gain` and `Offset` as `int` — no schema change needed.

## Detailed approach

### 1. VM property defaults

In `OrbitalFramingWizardVM.cs`:

```csharp
private int gain = -1;
public int Gain {
    get => gain;
    set { if (gain != value) { gain = value; RaisePropertyChanged(); } }
}

private int offset = -1;
public int Offset {
    get => offset;
    set { if (offset != value) { offset = value; RaisePropertyChanged(); } }
}
```

`-1` is NINA's well-known "use camera default" sentinel for these fields and
is what `SnapShotControlSettings.SetDefaultValues()` sets too.

### 2. XAML — blank-when-minus-one

The wizard's `OrbitalFramingWizardView.xaml` already merges
`NINA.WPF.Base;component/Resources/StaticResources/Converters.xaml`, which
defines `MinusOneToEmptyStringConverter`. Replace the two TextBoxes with:

```xaml
<TextBox Grid.Row="1" Grid.Column="1" Margin="3"
         Text="{Binding Gain, Mode=TwoWay,
                        UpdateSourceTrigger=LostFocus,
                        Converter={StaticResource MinusOneToEmptyStringConverter}}" />
<TextBox Grid.Row="2" Grid.Column="1" Margin="3"
         Text="{Binding Offset, Mode=TwoWay,
                        UpdateSourceTrigger=LostFocus,
                        Converter={StaticResource MinusOneToEmptyStringConverter}}" />
```

`UpdateSourceTrigger=LostFocus` matches NINA's snapshot view and avoids the
binding flipping back to `-1` mid-typing when the user clears the field.

(Skipping the `IntRangeRuleWithDefault` validation. The wizard VM doesn't
currently surface `CameraInfo.GainMin/GainMax` and adding that plumbing is
larger than this change warrants. The camera driver will already clamp.)

### 3. LiveCaptureSource — actually pass the values through

In `LiveCaptureSource.cs`, the final framing capture builds a
`CaptureSequence` but never sets `Gain` / `Offset`. Add them so the user's
input (or `-1` for default) makes it to the camera:

```csharp
var captureSeq = new CaptureSequence(...);
captureSeq.Gain = exposure.Gain;
captureSeq.Offset = exposure.Offset;
```

The plate-solve loop higher up keeps using `plateSolveSettings.Gain` —
that's by design and not what the user is configuring in the wizard.

`XisfStubCaptureSource` doesn't capture against a real camera so it can
remain a no-op for these fields.

## Verification

1. Build clean: `dotnet build NINA.Joko.Plugin.Orbitals.sln -c Release`.
2. Launch `TestApp` or run inside NINA, open the wizard. Confirm:
   - Gain and Offset TextBoxes are **blank** on first open.
   - Typing a value into either, tabbing out, then re-opening the wizard
     keeps that value (within the session — these aren't persisted).
   - Clearing a TextBox (selecting all, Delete, Tab) returns the field
     to blank and the VM property to `-1`.
3. Capture a frame with the fields blank. Verify in the FITS/XISF header
   or NINA log that the camera used the gain/offset configured under
   *Options → Equipment → Camera*.
4. Capture a frame with explicit values. Verify the header reflects the
   typed value (different from the camera-settings default).
