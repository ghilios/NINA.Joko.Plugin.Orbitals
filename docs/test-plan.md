# Implement unit tests for `NINA.Joko.Plugin.Orbitals`

## Context

The scaffold (NUnit project + coverlet) is in place and the CI workflow is wired but has no tests to run. This plan fills the project with **correctness-first** unit tests across the codebase. The goal is to validate behavior against independent references (IAU/SOFA constants, JPL Horizons snapshots, textbook worked examples) rather than re-running the implementation and asserting its own output. Where expected-correct behavior differs from current implementation, those tests are marked `[Explicit]` and listed in the "Bug candidates" section below for the user to review.

## Test design principles

1. **Correctness, not mirror.** Each assertion derives from an independent source: IAU 2006 constants, NOVAS/SOFA reference outputs (computed once by hand and committed), JPL Horizons text snapshots, textbook worked examples (Curtis, *Orbital Mechanics for Engineering Students*; Vallado, *Fundamentals of Astrodynamics*; Meeus, *Astronomical Algorithms*). Cite the source in a `// REF:` comment on each non-obvious expectation.
2. **Parameterized over enumerated.** Use `[TestCase]` / `[TestCaseSource]` so each boundary, sign, and unit-convention case is its own row. Failures point to the specific input.
3. **Round-trip when possible.** For coordinate parsers, unit converters, and serializers: assert `f(f^-1(x)) ~= x` to a stated tolerance; that catches lossy paths the literal-value tests miss.
4. **Tolerances stated explicitly.** Floating-point compares use FluentAssertions' `.BeApproximately(expected, tolerance)` with the tolerance derived from the calculation (e.g., Kepler's-equation solver tolerance = 1e-11 rad, so test tolerance >= 1e-10 rad).
5. **Bug-candidate handling.** When the implementation's apparent intent and observed behavior diverge, the test asserts the **correct** value, is marked `[Explicit, Category("BugCandidate")]`, and carries a `// SUSPECTED BUG:` comment pointing at the file:line. Default CI stays green; the user runs `dotnet test --filter Category=BugCandidate` (or unticks `[Explicit]`) to review.

## Bug candidates surfaced during exploration (review these first)

These are flagged for the user's review **before** test implementation begins, since each materially changes whether a test passes-as-written or fails-as-written.

1. **`Kepler.cs:409`** - `q_Perihelion_au = (1 + ecc) * a_SemiMajorAxis_au.Value`. **Perihelion distance = a(1 - e)**, not a(1 + e); the current expression gives aphelion. Triggers only on the fallback branch when `q_Perihelion_au` isn't pre-populated, so most live paths sidestep it - but anything that does fall through gets the maximum distance instead of the minimum. **High confidence bug.**
2. **`JPLAccessor.cs:258`** - `mapper.Property(x => x.H, headerLengths[9] + 1)` duplicates the `H` mapping from line 257. Standard JPL un-numbered asteroid format has `H` followed by `G` (magnitude slope parameter). Either `JPLUnnumberedAsteroidElements` is missing a `G` property and column 10 silently overwrites `H` (real bug), or `G` exists and the lambda is a typo. Verify the response class definition; near-certain bug either way.
3. **`PositiveIntegerRule.cs:25`** - Class is named "PositiveInteger" but only checks `int.TryParse`; accepts `0`, `-1`, etc. Error message says "or unlimited" but "unlimited" handling lives in a sibling rule. Either the validator is misnamed (intent is just "integer") or it's missing a `> 0` check. Looks like dead/wrong code - possibly always paired with another rule in XAML, but as a standalone validator it does nothing meaningful.
4. **`Kepler.cs:401`** - Hyperbolic distance formula `r = a * (e*cosh(E) - 1)`. For e > 1, the conventional sign of `a` matters. If `a` is stored as a positive scalar (semi-latus rectum convention or |a|), this is correct; if stored as a negative value (true semi-major axis for hyperbolic), `r` becomes negative. Worth a reference-data test before declaring a bug - flag for verification, not yet declared a bug.
5. **`JPLAccessor.cs:485-495` (`CalendarDateAndFractionToJulian`)** - `Math.Abs(dateAndFraction)` then reapplies sign to year only. For negative dates (BCE), month/day signs are dropped silently. And `dayPart * 24` can exceed 24 with float rounding (no clamp). Edge cases; either fix or document the supported domain.
6. **`OrbitalElementsAccessor.cs:390/395`** - `RotateEcliptic(-J2000MeanObliquity)` then later `+J2000MeanObliquity`. Sign convention easy to flip; needs a reference-data test against a known JWST ephemeris snapshot to confirm.
7. **`AstrometricConstants.NormalizeRadians`** - Boundary mapping: both `+pi` and `-pi` map to `+pi` (range is `(-pi, +pi]`). Likely intentional; the test will assert this convention so any future change is caught.

## Test buildout - phased

Each phase is independently deliverable. Folder structure under `NINA.Joko.Plugin.Orbitals.Tests/` mirrors the plugin's source folders.

### Phase 1 - Foundation

**`TestHelpers/`**
- `MockBuilders.cs` - typed `Mock<>` factories for the recurring NINA interfaces: `IProfileService` (loaded with a fixed lat/lon/elevation/horizon), `ITelescopeMediator`, `IGuiderMediator`, `IApplicationMediator`, `INighttimeCalculator`, `IOrbitalsOptions`. Each factory returns `(Mock<T> mock, T sut)` so tests can both customize and consume cleanly.
- `Fixtures.cs` - well-known `OrbitalElements` constants: Halley (1P/Halley standard elements at a published epoch), Ceres (current MPC values), a synthetic circular orbit at e=0, a synthetic hyperbolic orbit at e=1.5. All values cited to source.
- `ReferenceData/` directory - committed JPL/MPC snapshot files (text), embedded as `EmbeddedResource` in the csproj.

### Phase 2 - Pure orbital math (the meat)

**`Calculations/KeplerTests.cs`** - assertions against textbook + JPL data.
- **Kepler's equation solver, elliptic** (`CalculateOrbitalElements`): `[TestCase]` rows for `(M, e) -> E` from Curtis Example 3.1 (M=3.6029, e=0.37255 -> E=3.4794), plus rows at e=0, 0.1, 0.5, 0.9. Tolerance 1e-9 rad.
- **Kepler's equation solver, hyperbolic**: rows from Curtis Example 3.5. Tolerance 1e-9 rad.
- **Parabolic (Barker)**: rows from Meeus Ch. 35 worked example for comet C/1980 E1 or similar. Tolerance 1e-7 rad.
- **True anomaly from eccentric anomaly**: verify `v = 0` when `E = 0`, `v = pi` when `E = pi`, half-angle formula at `E = pi/2` for several eccentricities.
- **Periapsis distance**: `r(E=0) = a(1-e)` for elliptic; `r(E=0) = a(e-1)` for hyperbolic. **This row will surface the line 409 bug if it ever runs that fallback path; also a direct standalone test asserting `OrbitalElements.q_Perihelion_au` matches `a(1-e)` after the fallback fires - marked `[Explicit, Category("BugCandidate")]`.**
- **End-to-end propagation for Halley**: take published elements at epoch, propagate to a date for which JPL Horizons has a snapshot, compare ecliptic position. Tolerance ~1 arcsec.
- **`GetApparentPosition` topocentric**: for a known observer (e.g., lat 40 N, lon -105 W, elev 1600m) and a known object/time, compare RA/Dec against a JPL Horizons APPARENT-coords snapshot. Tolerance ~5 arcsec (accounts for refraction differences).

**`Calculations/AstrometricConstantsTests.cs`**
- `NormalizeRadians` table: rows for 0, pi, -pi, 2pi, -2pi, 3pi, -3pi, 0.5, pi+0.001, -pi-0.001, pi/2 - assert IAU convention (`-pi < r <= pi`). Includes the boundary case `NormalizeRadians(-pi) == +pi` (asserts the current convention so future regressions are caught).
- `J2000MeanObliquity` ~= 23.4392911 deg. Tolerance 1e-7 deg. **REF**: IAU 2006.
- Unit conversions: `KM_PER_AU` and `M_PER_AU` match IAU 2012 nominal AU (1.49597870700e11 m). Note current value is `1.49597870691e11` - flag as a 0.06 km drift; almost certainly fine, but call it out. **`[Category("BugCandidate")]`** as a low-priority precision note.

### Phase 3 - Converters

**`Converters/RadianToDegreeConverterTests.cs`** - `[TestCase]` rows for 0 -> 0, pi -> 180, pi/2 -> 90, 2pi -> 360, -pi -> -180, NaN -> null, non-numeric string -> null.

**`Converters/JulianToDateTimeConverterTests.cs`** - `[TestCase]` rows for JD 2451545.0 -> 2000-01-01T12:00 (J2000.0), JD 2440587.5 -> 1970-01-01T00:00 (Unix epoch), JD 0 -> -4712-01-01T12:00 (proleptic Julian start), non-numeric -> empty string. **REF**: USNO.

**`Converters/DateTimeMinToNeverConverterTests.cs`** - `DateTime.MinValue -> "Never"`, `null -> "Never"`, `2024-03-15T10:30 -> "<culture-formatted-string>"` (assert via `OrbitalsPlugin.SystemCultureInfo` set to `InvariantCulture` in a `[OneTimeSetUp]`).

**`Converters/EnumStaticDescriptionValueConverterTests.cs`** - Define a local test enum with `[Description]` attrs (`[Description("Foo")]`, `[Description("LblMySetting")]`); assert non-"Lbl" returns the description verbatim, and "Lbl"-prefixed delegates to `Loc.Instance` (mocked or via a known NINA key).

### Phase 4 - ValidationRules

**`ValidationRules/HoursExRuleTests.cs`** - Rows for `-23 -> valid`, `23 -> valid`, `-24 -> invalid`, `24 -> invalid`, `0 -> valid`, `100 -> invalid`, `"abc" -> invalid`, `null -> invalid`. Class name "HoursEx" reads as "hours exclusive of +/-24" -> exclusive bounds are likely correct (the test encodes this; if the user disagrees, one boundary row flips).

**`ValidationRules/PositiveIntegerRuleTests.cs`** - Rows asserting the correct behavior given the name: `1 -> valid`, `100 -> valid`, `0 -> invalid`, `-5 -> invalid`, `"abc" -> invalid`, `null -> invalid`. **The `0` and `-5` rows are marked `[Explicit, Category("BugCandidate")]`** since the current implementation accepts them. See bug candidate #3.

**`ValidationRules/PositiveIntegerOrInfiniteRuleTests.cs`** - `"unlimited" -> valid`, `"1" -> valid`, `"0" -> invalid`, `"-5" -> invalid`, `"abc" -> invalid`, `null -> invalid`.

**`ValidationRules/ValidTLERuleTests.cs`** - Use a real known-good TLE (ISS, with date noted as "TLE from celestrak {date}, kept for parser-only test"), assert valid. Malformed line counts (1 line, 4 lines) -> invalid. Garbage strings -> invalid. Null -> invalid.

### Phase 5 - Utility

**`Utility/InputCoordinatesExTests.cs`** - **HIGHEST PRIORITY in this folder.** Round-trip tests for:
- Sirius RA 06h45m08.917s, Dec -16d42'58.02"
- Polaris RA 02h31m49.09s, Dec +89d15'50.79" (near pole)
- A target at RA 23h59m59s (24h-wrap edge)
- Dec exactly +90 deg and -90 deg
- Negative RA inputs
- Minute carry: 59s + rounding -> 1 minute / 0 seconds
- Hour carry: 23h59m59.9s rounded
Cross-check by setting each field individually and reading the assembled `Coordinates.RA` / `.Dec` against a hand-computed expected. Use a tolerance of 1e-6 deg for the round-trip.

**`Utility/ListExtensionsTests.cs`** - `BinarySearchWithKey<T,K>` on `int` keys: empty list -> -1 or insertion point per docs, single-element matches/no-matches, found at start/middle/end, not-found (assert returns negative index per `List.BinarySearch` convention if that's what it mimics - verify by reading impl first), key-extractor that yields a derived value.

**`Utility/DistanceTests.cs`** - Boundary at 0.1 AU: 0.099 AU -> displayed in km (1.481e7 km); 0.101 AU -> displayed in AU; INPC `PropertyChanged` fires when AU setter changes value.

**`Utility/TleUtilTests.cs`** - `ParseTle`:
- Valid 3-line TLE (name + L1 + L2) -> returns Tle.
- Valid 2-line TLE (no name) -> returns Tle.
- 1-line input -> throws/returns false.
- 4-line input -> throws/returns false.
- TLE with checksum-corrupted L2 -> behavior depends on SGP.NET; pin the current behavior and document.
`TelescopeSupportsShiftRate(TelescopeInfo)`: rows covering each property combination that drives the boolean.

**`Utility/TrigramStringMapTests.cs`** - Use SQLite in-memory (`Mode=Memory&Cache=Shared`):
- Insert N entries; `Lookup(exactName)` returns the entry.
- `Lookup(unknown)` -> null / throws (assert observed).
- `Lookup(ambiguousSubstring)` with multiple matches -> `DuplicateKeyException`.
- `Search(prefix)` returns multiple ordered results.
- Disposal closes the connection (verify via second Lookup throws).
Marked `[Category("RequiresSqlite")]` so it can be filtered if needed.

**`Utility/EnumExtensionsTests.cs`**
- `ToDescriptionString(enumValue)` for a locally-defined test enum (non-"Lbl" description).
- `ApplyQuirks(SiderealShiftTrackingRate, QuirksModeEnum.None)` -> unchanged.
- `ApplyQuirks(SiderealShiftTrackingRate, QuirksModeEnum.EQMOD)` -> expected adjustment derived from sidereal rate definition (`SIDEREAL_RATE_ARCSEC_PER_SI_SEC = 15.0410686`). Compute expected by hand from the constant; if it diverges from implementation, flag.

### Phase 6 - Parsing (JPL / MPC)

**`Calculations/JPLAccessorParsingTests.cs`** - Use committed `TestHelpers/ReferenceData/jpl_*.txt` fixtures.
- Comet response parse -> first record matches a known comet's elements (assert per-field: name, q, e, i, w, node, tp, epoch).
- Numbered asteroid parse -> first record matches Ceres's known elements at the snapshot's epoch.
- Un-numbered asteroid parse -> first record's H and **G** are both populated. **This row exposes bug #2** (currently G column would silently overwrite H); marked `[Explicit, Category("BugCandidate")]` until the user reviews.
- JWST vector table parse -> first row's `(x, y, z, vx, vy, vz)` matches the fixture; `Length` matches `$$SOE`/`$$EOE` block.
- Malformed input (truncated row, missing header) -> emits `ParseError` event with sensible detail.
- `CalendarDateAndFractionToJulian`: rows for known calendar->JD pairs from USNO. Bug-candidate rows for negative `dateAndFraction` and for `dayPart` very close to 1.0.

**`Calculations/MPCAccessorParsingTests.cs`** - Same shape, against `mpc_comets_sample.txt`.
- One row per parsed field on a single known comet.
- Empty/missing optional fields handled (e.g., absent periodic number).
- `GetCometElementsLastModified` parsing if it does any header processing.

### Phase 7 - Stateful & I/O

**`Calculations/OrbitalElementsAccessorTests.cs`**
- `Load()` from a small protobuf gzip fixture (build the fixture in `[OneTimeSetUp]` by serializing a tiny in-memory map, write to a temp path, point the accessor at it).
- `Search(type, partialName)` returns expected matches.
- `Get(type, exactName)` returns the entry.
- `Update(type, ...)` replaces atomically (the .new -> .swap -> delete sequence - verify behavior on a temp dir).
- `GetObjectPV(...)` with a deterministic profile mock and a fixture orbital element: assert RA/Dec/topocentric-distance match a hand-computed expected from `Kepler.CalculateOrbitalElements` + `GetApparentPosition` for the same inputs. Tolerance ~10 arcsec.
- `GetSolarSystemBodyPV(SolarSystemBody.Jupiter, fixedDate)` vs. JPL Horizons Jupiter ephemeris snapshot. Tolerance ~30 arcsec (NOVAS internals).
- `GetPVFromTable(...)` linear-acceleration interpolation: build a 3-row fixture table with known constant acceleration, query midway between rows, expected = analytical position. Tolerance 1e-8 AU. **Also verify `RotateEcliptic` signs** by comparing a known ecliptic->equatorial-rotated vector against a hand-computed SOFA rotation. Bug candidate #6 is exposed here.

### Phase 8 - SequenceItems

**`SequenceItems/SetTelescopeShiftRateTests.cs`** - `Execute(...)`: mock `ITelescopeMediator`, assert `SetCustomTrackingRate` called once with the expected `SiderealShiftTrackingRate` (after `ApplyQuirks` per current options). Cancellation: cancel token -> no call. Validation: no telescope connected -> throws / logs / no-op (pin observed behavior; flag if it differs from intent).

**`SequenceItems/SetGuiderShiftRateTests.cs`** - Symmetric to telescope, against `IGuiderMediator.SetShiftRate`.

**`SequenceItems/SetTelescopeShiftRateTriggerTests.cs`** / **`SetGuiderShiftRateTriggerTests.cs`** - Trigger predicate truth tables: telescope connected + tracking + custom rate available -> fire; missing any -> no-op.

**`SequenceItems/StopGuiderShiftTests.cs`** - One call to `IGuiderMediator.SetShiftRate(0, 0)` or whatever the "stop" sentinel is.

**`SequenceItems/TleSlewTests.cs`** - Heavy. Mock telescope + guider + clock (`ICustomDateTime` -> `FixedDateTime`). Verify:
- Position computed at `Now + slewAheadSeconds`.
- `SlewToCoordinatesAsync` called with that future position.
- `SetCustomTrackingRate` called after slew with the propagated SiderealShiftTrackingRate.
- Refresh loop calls `SetCustomTrackingRate` at the configured cadence and stops on cancellation.
- Pulse-guide path (if used) verified separately.
- Error paths: slew failure -> tracking not enabled; mediator throws -> propagated.

**`SequenceItems/OrbitalsContainerBaseTests.cs`** - Subclass under test in a fake derived class. Verify:
- Profile `LocationChanged` event triggers a position refresh.
- `HorizonChanged` triggers a refresh.
- Weak-reference unsubscribe on Dispose / leaves no leak (assert event handler count after disposal).
- Deserialization hook restores `InputCoordinatesEx` correctly.

**`SequenceItems/OrbitalObjectContainerTests.cs`** / **`ManualTLEContainerTests.cs`** / **`JWSTContainerTests.cs`** / **`SolarSystemBodyContainerTests.cs`** - Each: construction wires the right accessor event, target updates on element change, dispose cleans up.

### Phase 9 - ViewModels

**`ViewModels/OrbitalSearchVMTests.cs`** - Use `Task.Delay` faking via a virtualized time source if available; otherwise allow the real 100ms debounce in tests (slow but tolerable).
- Typing increments search; results populate.
- Popup visible iff results > 0.
- Cancellation of in-flight search when new input arrives.

**`ViewModels/OrbitalsVMTests.cs`** - Per command:
- `UpdateCometElements` (and asteroid variants): mock `IJPLAccessor` / `IMPCAccessor` -> returns canned response, verify `IOrbitalElementsAccessor.Update` called with the right type and counts.
- Download failure path -> user-facing error state set, no `Update` call.
- Slew-and-track command: mock mediators; verify it computes ahead-time position, slews, starts tracking, and starts the refresh loop. Cancellation stops the loop.
- Quirks application: mode = EQMOD -> adjusted rate sent.
- Property-changed propagation: derived property updates fire `PropertyChanged`.

### Phase 10 - Enums

**`Enums/SearchObjectTypeEnumExtensionsTests.cs`** - `ToOrbitalObjectTypeEnum()` rows:
- `Comet -> Comet`, `NumberedAsteroids -> NumberedAsteroids`, `UnnumberedAsteroids -> UnnumberedAsteroids` -> valid.
- `SolarSystemBody`, `JWST`, `ManualTLE` -> expected throw (`InvalidOperationException` or whatever it raises - pin the type).

## Test fixtures (committed as embedded resources)

Under `NINA.Joko.Plugin.Orbitals.Tests/TestHelpers/ReferenceData/`:

- `jpl_comets_sample.txt` - first ~20 rows of a real JPL comet bundle (downloaded once, dated in a `// captured YYYY-MM-DD` comment in the test that uses it).
- `jpl_numbered_asteroids_sample.txt` - first ~20 rows (must include Ceres).
- `jpl_unnumbered_asteroids_sample.txt` - first ~20 rows (selected to include rows where H != G so bug #2 is observable).
- `jpl_jwst_vectors_sample.txt` - Horizons vector-table response across ~5 dates.
- `mpc_comets_sample.txt` - first ~20 rows of real MPC comet bundle.
- `tle_iss.txt` - single known-good ISS TLE (with capture date noted).
- `halley_ephemeris.json` - JPL Horizons (x, y, z, RA, Dec) for Halley at 5 dates spanning its 2024 elements.
- `ceres_ephemeris.json` - same for Ceres.
- `jupiter_ephemeris.json` - same for Jupiter (drives the `GetSolarSystemBodyPV` test).

Mark all with `<EmbeddedResource Include="TestHelpers/ReferenceData/**" />` in the test csproj and load via `Assembly.GetManifestResourceStream`.

## Verification

After each phase:

```
cmd.exe /c "dotnet test NINA.Joko.Plugin.Orbitals.Tests\NINA.Joko.Plugin.Orbitals.Tests.csproj -c Debug --no-restore"
```

- All non-`[Explicit]` tests must pass; CI stays green.
- To review bug candidates: `dotnet test --filter Category=BugCandidate` (these are `[Explicit]` so won't run otherwise) - each failure indicates either a real bug to fix in code or a behavior to confirm intentional (in which case the test is updated to assert current behavior and the `[Explicit]` lifted).
- Coverage report on `develop`: workflow's existing PR comment shows %; targets are informational only this round (no enforced threshold).

End-to-end on push to a feature branch: PR comment from `tests.yml` shows test summary + coverage table.
