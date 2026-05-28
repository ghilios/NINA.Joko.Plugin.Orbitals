# Code review findings — plan/orbital-framing-wizard

Delete any entry you do NOT want fixed. The remaining entries will be addressed.

---

## 1. RecalculateOffsets contaminates Sep/PA with body motion between capture and drag

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:611`

RecalculateOffsets uses live body position via `PositionAt(DateTime.UtcNow)` but
framingTarget is anchored to CapturedImageCoordinates (frozen at capture time).
For fast-moving TLE bodies, Sep/PA gets contaminated by the body's motion
between capture and the user dragging the rectangle.

**Failure scenario:** User captures a fast TLE target at T=0 (say 10 arcsec/sec).
They adjust framing at T=30s. RecalculateOffsets reads bodyCoords at T=30 while
framingTarget is anchored at T=0 plate-solve coords → Sep absorbs the 300 arcsec
of body motion that has nothing to do with the user's framing intent. The
exported Sep/PA places the framing rectangle ~300 arcsec offset at slew time,
the opposite of the plugin's primary use case.

---

## 2. OrbitalOffsetMath.ApplyOffset hard-codes Epoch.J2000 on the returned Coordinates

**File:** `NINA.Joko.Plugin.Orbitals/Calculations/OrbitalOffsetMath.cs:111`

ApplyOffset returns Coordinates tagged Epoch.J2000 regardless of `from.Epoch`,
silently re-epoching the result. The new SetOffsetFromRADec in the same diff
disagrees by preserving origin.Epoch.

**Failure scenario:** OrbitalsContainerBase.RefreshCoordinates calls
ApplyOffset(originalTarget, …). For ManualTLE bodies originalTarget carries
Epoch.JNow (container ctor uses `telescopeMediator.GetInfo().EquatorialSystem`).
The shifted Coordinates returned with Epoch.J2000 is assigned to
Target.InputCoordinates.Coordinates, switching the epoch under downstream Slew /
Center-and-Rotate items — pointing error up to tens of arcsec from precession
at modern dates.

---

## 3. Subclass Clone() implementations don't copy new offset fields

**Files:**
- `NINA.Joko.Plugin.Orbitals/SequenceItems/SolarSystemBodyContainer.cs:81`
- `NINA.Joko.Plugin.Orbitals/SequenceItems/JWSTContainer.cs:80`
- `NINA.Joko.Plugin.Orbitals/SequenceItems/OrbitalObjectContainer.cs:161`
- `NINA.Joko.Plugin.Orbitals/SequenceItems/ManualTLEContainer.cs:131`

All four Clone() implementations copy Target/InputCoordinates but never copy
the new `OffsetSeparationArcsec` / `OffsetPositionAngleDeg` fields — non-wizard
clones silently zero the user's framing offset.

**Failure scenario:** User saves a SolarSystemBodyContainer template with
Sep=120″/PA=45° and duplicates it via the sequencer UI. The clone has
Sep=0/PA=0; the mount slews to the body's exact center, losing the framing
entirely with no warning. (Save/load via JSON still works because the fields
are `[JsonProperty]`; only Clone is broken.)

---

## 5. skyMapAnnotator PropertyChanged handler never unsubscribed → VM leak

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:133`

PropertyChanged handler on skyMapAnnotator is `+=` subscribed in the ctor via a
lambda capturing `this`, but Dispose() never `-=` unsubscribes — the annotator
keeps the wizard VM rooted indefinitely.

**Failure scenario:** User opens the wizard, closes it. Dispose() stops timers
but the annotator's event delegate still references the VM closure.
CapturedImage / BackgroundImage bitmaps, SkyMapAnnotator state, and all
captured ICaptureSource references stay alive; opening/closing the wizard
repeatedly accumulates VM instances and pinned bitmap memory.

---

## 6. ExportToSequencerAsync outer try has no catch — exceptions silent

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:622`

Outer try (line 622) has only `finally`, no `catch`. Inner try/catches cover
ConstructDefaultContainer and AddAdvancedTarget, but GetContainerTypeForObject
(throws InvalidOperationException for unknown subclasses), `template.Clone()`,
and PopulateContainerSpecificFields all run inside the outer try with no catch
— exceptions propagate out as unobserved task faults with no user-visible
notification.

**Failure scenario:** Future OrbitalsObjectBase subtype not handled by
GetContainerTypeForObject → InvalidOperationException thrown; AsyncRelayCommand
swallows it as an unobserved fault. The Export button silently appears to do
nothing; the only signal is a NINA debug-log line.

---

## 7. Negative OffsetSeparationArcsec silently no-op while display shows |abs|

**File:** `NINA.Joko.Plugin.Orbitals/SequenceItems/OrbitalsContainerBase.cs:282`

RefreshCoordinates only applies the offset when `offsetSeparationArcsec > 0.0`;
the setter accepts any value (no clamp), and OffsetSeparationDisplay (line 183)
uses `Math.Abs` — a negative value displays as positive but produces no actual
offset.

**Failure scenario:** User types `-30` into the unvalidated Separation TextBox;
the display shows `00° 00′ 30.0″` (Math.Abs), but RefreshCoordinates skips the
offset entirely and the mount slews to the body's bare position. Displayed
offset and actual framing disagree, with no visual cue.

---

## 9. MaxExposureSeconds NaN setter raises PropertyChanged every 2s forever

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:791`

MaxExposureSeconds defaults to `double.NaN` and the setter guards with
`if (maxExposureSeconds != value)`. IEEE 754: `NaN != NaN` is always true, so
the timer tick raises PropertyChanged every 2 seconds forever when
MaxExposureSeconds stays NaN.

**Failure scenario:** Wizard opens before camera/scope profile values populate
(pixscale unavailable) so MaxExposureSeconds remains NaN. Every 2-second
DispatcherTimer tick still raises PropertyChanged, causing redundant binding
refresh of the bound TextBlock and any converter work behind it — death by a
thousand cuts on the UI thread.

---

## 10. FinalPositionAngle propagates NaN to exported Target.PositionAngle

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:518`

FinalPositionAngle is computed via `(((360.0 - frame.PositionAngleDeg) % 360.0)
+ 360.0) % 360.0` with no NaN/Inf guard; the result is later written to
`dsoForPA.Target.PositionAngle` on export.

**Failure scenario:** A plate solver that returns success but PositionAngle=NaN
(some solvers do this on partial solves) → NaN propagates through subtraction
and modulo, FinalPositionAngle stays NaN, Target.PositionAngle is set to NaN
on the exported container, poisoning downstream slew/rotate math with no
warning.

---

## 11. ExportToSequencerAsync writes Sep then PA — intermediate refresh uses stale PA

**File:** `NINA.Joko.Plugin.Orbitals/ViewModels/OrbitalFramingWizardVM.cs:654`

Writes `OffsetSeparationArcsec` then `OffsetPositionAngleDeg` as two separate
property writes. Each container setter synchronously fires RaiseOffsetChanged →
RefreshCoordinates, so the first call applies the new Sep with the
freshly-cloned container's PA=0; the second corrects it. The same defect class
affects `SetOffsetFromRADec` (OrbitalsContainerBase.cs:213).

**Failure scenario:** Export with Sep=300″/PA=45°: first setter fires
RefreshCoordinates with Sep=300/PA=0 — ApplyOffset places the target 300 arcsec
due North. Second setter fires RefreshCoordinates with the correct PA=45°. The
first refresh can trip the 'Invalid dec' notification path (line 286-294) for
high-Dec bodies, and any consumer observing Target.InputCoordinates between
the two writes sees the wrong intermediate value.

---

## 12. XisfStubCaptureSource silently falls back to synthetic gradient on load failure

**File:** `NINA.Joko.Plugin.Orbitals/Imaging/XisfStubCaptureSource.cs:122`

When NINA's image-load pipeline fails on the user-picked file, the exception is
logged at Warning level and silently replaced by a synthetic 640×480 gradient
with random PA — the user sees a 'successful' capture but is framing against
placeholder data.

**Failure scenario:** Stub-mode user picks a corrupt/unsupported file
(truncated FITS, RAW without a configured decoder, locked file).
imageDataFactory.CreateFromFile throws; the catch logs warning and falls
through to the synthetic-bitmap path with `rng.NextDouble() * 360.0` PA.
Wizard reports successful capture, user frames against a meaningless gradient,
export carries bogus FinalPositionAngle. No notification surfaces.

---

## 13. XisfStubCaptureSource throws FileNotFoundException for unsupported file types

**File:** `NINA.Joko.Plugin.Orbitals/Imaging/XisfStubCaptureSource.cs:86`

Throws FileNotFoundException when the file is *unsupported* but exists;
OrbitalFramingWizardVM.cs:532 catches FileNotFoundException and shows
`'XISF stub frame not found: {file}'`, hiding the real cause.

**Failure scenario:** User picks a .bmp the NINA image loader doesn't support.
Line 86 throws `new FileNotFoundException("File type not supported by NINA's
image loader: {path}", path)`. Wizard notification reads 'XISF stub frame not
found: foo.bmp' even though the file is right there — user wastes time hunting
an imaginary path problem.

---

## 14. LiveCaptureSource forwards exposure.Gain raw (0 != camera default)

**File:** `NINA.Joko.Plugin.Orbitals/Imaging/LiveCaptureSource.cs:149`

framingSeq.Gain and framingSeq.Offset are forwarded raw from
OrbitalFramingExposureSettings, but the property type is `int` (default 0),
not int with a -1 sentinel. The comment claims '-1 means use camera default'
but an empty Gain field yields 0, which most camera drivers treat as 'gain 0'
(lowest) — not the camera default the user expects.

**Failure scenario:** User leaves Gain blank in the wizard UI (VM keeps
initial 0). LiveCaptureSource sends Gain=0 to the camera. The camera captures
at minimum gain instead of the user's profile default; the framing frame is
much darker than expected, possibly looking like a failed capture.

---

## 15. OnOrbitalsDeserialized pops Notification.ShowWarning at sequence-load time

**File:** `NINA.Joko.Plugin.Orbitals/SequenceItems/OrbitalsContainerBase.cs:166`

OnOrbitalsDeserialized triggers RaiseOffsetChanged → RefreshCoordinates
synchronously inside the JSON.NET deserialization callback, which can pop an
'Invalid dec after applying offset' Notification.ShowWarning at sequence-load
time before the user has touched anything.

**Failure scenario:** User loads a sequence with a saved high-Dec orbital body
and an offset that pushes |Dec|>90°. OnOrbitalsDeserialized runs
RefreshCoordinates inside the deserialization callback; the warning toast
fires, and the just-deserialized state is mutated by the offset-reset path
inside the deserialize step. Noisy popup on every NINA startup for those users;
potentially confusing 'invalid' message about a sequence they didn't modify.
