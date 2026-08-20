# Fix solar-system-body coordinates: topocentric place + TT time scale

## Context

A user reported that the Moon's RA/Dec from Orbitals disagrees with Stellarium at the same
instant, by enough to put the target outside the telescope FOV.

I reproduced and root-caused this against JPL Horizons (DE441), driving the plugin's real
code through the test project (which already ships NOVAS31lib + JPLEPH via `ASCOM.Tools`).

### Measured: the Moon is off by 48.4 arcmin

At 2026-08-19 00:00 UT from Greenwich, `OrbitalElementsAccessor.GetSolarSystemBodyPV`
returns RA 219.486461, Dec -20.742528. Horizons' topocentric apparent place is
RA 218.914666, Dec -21.347108 — a separation of **2903″ (48.38′)**.

That figure matches Horizons' own geocentric-vs-topocentric parallax for the Moon at that
instant (2916″ / 48.61′) almost exactly. Residual against Horizons' *geocentric* apparent
place is only 35″.

### Two distinct root causes

**1. The position is geocentric, not topocentric.** `GetSolarSystemBodyPV`
(`Calculations/OrbitalElementsAccessor.cs:276`) calls `NOVAS.PlanetApparentCoordinates`,
which wraps NOVAS `app_planet` — a strictly **geocentric** apparent place. There is no
observer on the Earth's surface anywhere in that path, so diurnal parallax is never
applied. This is the FOV-busting error. Magnitude scales with distance:

| Body | Horizons geo-vs-topo parallax |
|---|---|
| Moon | 2916″ (48.6′) |
| Venus | 11.3″ |
| Sun | 7.8″ |
| Mars | 4.6″ |
| Jupiter | 1.3″ |
| Saturn | 1.0″ |

**2. A UTC Julian date is passed where a TT Julian date is required.** Both
`GetSolarSystemBodyPV:277` and `GetObjectPV:288` call `AstroUtil.GetJulianDate(asof)`
(UTC-based) and feed the result to code expecting TT — the local variables are even named
`jdtt` / `observerJdtt`. NINA provides `AstroUtil.GetJulianDateTT` for exactly this.
ΔT is 69.18 s, which costs 35.25″ on the Moon, 2.8″ on the Sun, 2.7″ on Venus, 1.9″ on
Mars. Same bug at `Kepler.cs:200` (`GetPVOnEarthSurface`) and `OrbitalsVM.cs:624`.

### Also measured: the tracking rate is wrong for the Moon

Because the rate is differenced from two geocentric positions, it misses the diurnal
parallax rate entirely: geocentric 1851″/hr RA, -607″/hr Dec vs topocentric 2090″/hr RA,
-431″/hr Dec. That is a **-239″/hr RA, -176″/hr Dec** rate error — roughly 0.08″/s of
uncorrected drift, i.e. ~5″ of trailing in a 60 s exposure.

### The asteroid / NEO path is essentially correct

`GetObjectPV` already does a topocentric, light-time-corrected reduction. Verified against
Horizons topocentric ICRF:

- **1 Ceres** (2.6 au), 2024-01-01: **1.20″**
- **99942 Apophis** (0.0135 au — 2M km, where parallax is 646″), 2029-04-10: **2.19″**,
  and it tracks the parallax correctly hour by hour (2.2″ / 2.4″ / 2.4″ at +0/+1/+2 h).

So no frame bug here — only the ΔT error. Switching that path to `GetJulianDateTT` takes
Ceres from **1.20″ → 0.02″** and Apophis from **2.19″ → 0.74″**.

### Validated fix

`NOVAS.Place(GetJulianDateTT(asof), obj, onSurfaceObserver, DeltaT(asof),
CoordinateSystem.Astrometric, Accuracy.Full)` reproduces Horizons topocentric ICRF to
**≤0.04″** for every body tested (Moon 0.01″, Mars 0.00″, Jupiter 0.04″, Venus 0.00″,
Saturn 0.04″, Sun 0.00″).

Astrometric J2000 is the right output frame because it is what the asteroid/comet path
already returns, and it is NINA's convention: `Coordinates.Transform(Epoch.JNOW)`
(`NINA.Astrometry/Coordinates.cs:122`) applies SOFA `Atci13`, i.e. full aberration +
light deflection + precession/nutation. Round-tripping the astrometric answer through that
transform reproduces Horizons' apparent place to **≤0.5″**.

This also fixes a user-visible inconsistency: `OrbitalsView.xaml:448` displays
`TargetCoordinates.RAString` with no epoch label, while asteroids report J2000 and solar
system bodies report JNOW — the same field currently means two things ~18′ apart.

Note: displayed Moon/planet RA/Dec will shift by ~18′ of precession relative to today, and
will now agree with Stellarium's **J2000** readout rather than its of-date readout.

---

## Changes

### 1. `Calculations/OrbitalElementsAccessor.cs` — rewrite `GetSolarSystemBodyPV`

Take observer location, and reduce with NOVAS `place`:

- Signature becomes
  `GetSolarSystemBodyPV(DateTime asof, SolarSystemBody body, Angle latitude, Angle longitude, double elevation, TimeSpan rateDriftDelta)`,
  matching the shape of the existing `GetObjectPV` right below it. Update
  `Interfaces/IOrbitalElementsAccessor.cs:88`.
- Build a `NOVAS.CelestialObject` (`Type = MajorPlanetSunOrMoon`, `Number = (short)body`)
  and a `NOVAS.Observer` with `Where = ObserverLocation.EarthSurface` and an `OnSurface`
  carrying lat/lon/elevation — the same struct-construction `AstroUtil.GetMoonPosition`
  uses (`NINA.Astrometry/AstroUtil.cs:527`).
- Call `NOVAS.Place(AstroUtil.GetJulianDateTT(asof), obj, observer, AstroUtil.DeltaT(asof),
  NOVAS.CoordinateSystem.Astrometric, NOVAS.Accuracy.Full, ref pos)`; throw on non-zero rc
  the way the other NOVAS wrappers in `Kepler.cs` do.
- Return `new Coordinates(Angle.ByHours(pos.RA), Angle.ByDegree(pos.Dec), Epoch.J2000)`.
- Compute the second sample at `asof + rateDriftDelta` through the *same* helper so the
  tracking rate picks up the diurnal parallax rate, then
  `SiderealShiftTrackingRate.Create(start, next, rateDriftDelta)` as today.
- `OrbitalPositionVelocity.Position` becomes the **topocentric** vector so the Distance
  readout is right (currently geocentric — 0.74% off for the Moon). Build it from
  `pos.Dis` and the returned RA/Dec rather than from `SkyPosition.RHat`, to avoid relying
  on `ByValArray` marshalling of an array field that is null on input:
  `X = Dis·cos(dec)·cos(ra)`, `Y = Dis·cos(dec)·sin(ra)`, `Z = Dis·sin(dec)`.
  Only `.Distance` is consumed (`OrbitalsVM.cs:810`, `OrbitalsContainerBase.cs:345`), and
  this puts the frame in line with the asteroid path's `Position`.

Factor the per-instant reduction into one private helper returning `(Coordinates, RectangularCoordinates)`,
mirroring how `ApparentTopocentricWithLightTime` is factored out of `GetObjectPV`.

`NOVAS.PlanetApparentCoordinates` and `NOVAS.BodyPositionAndVelocity` are no longer needed
in this method.

### 2. `Calculations/SolarSystemBodyObject.cs` — supply the observer

Add an `IProfileService` constructor parameter and read lat/lon/elevation from
`profileService.ActiveProfile.AstrometrySettings` in `CalculateObjectPosition`, exactly as
`OrbitalElementsObject.CalculateObjectPosition` (`Calculations/OrbitalElementsObject.cs:57-64`)
already does. Thread it through `Clone()`.

Two production construction sites, both of which already have `profileService` in scope:
`ViewModels/OrbitalsVM.cs:861` and `SequenceItems/SolarSystemBodyContainer.cs:61`.
About nine test construction sites in `Tests/ViewModels/OrbitalFramingWizardExportTests.cs`
plus the `Moq` setup at line 119 need the extra argument.

### 3. Time-scale fixes (`GetJulianDate` → `GetJulianDateTT`)

- `Calculations/OrbitalElementsAccessor.cs:288` — `observerJdtt` in `GetObjectPV`.
  This is the change measured at Ceres 1.20″ → 0.02″.
- `Calculations/Kepler.cs:200` — `GetPVFromObserver`. Careful here:
  `GetTopocentricJ2000Position` reaches this via
  `GetPVOnEarthSurface(NOVAS.JulianToDateTime(observerJdtt), ...)`, so once `observerJdtt`
  is a *TT* julian date, converting it back to a DateTime and re-applying `GetJulianDateTT`
  double-counts the offset and displaces the observer by ~69 s of Earth rotation (worth
  ~2 arcsec on a close NEO). Add TT-julian-date overloads of `GetPVFromObserver` /
  `GetPVOnEarthSurface` and have `GetTopocentricJ2000Position` pass `observerJdtt` straight
  through; the DateTime overloads keep converting with `GetJulianDateTT` for callers that
  genuinely hold a wall-clock time (`GetPVFromTable`).
- `ViewModels/OrbitalsVM.cs:624` — display-only, but same bug.
- `Calculations/OrbitalElementsAccessor.cs:399` — `GetPVFromTable` (JWST), for consistency
  with the vector table's TDB epochs.

Leave `Kepler.GetTopocentricJ2000Position`'s XML doc accurate: it still returns a
geometric/astrometric direction, and the caller still owns light-time.

### 4. `Calculations/MPCAccessor.cs:56` — latent epoch bug

`DateTime.ParseExact(epochString, "yyyyMMdd", ...)` (line 156) yields
`DateTimeKind.Unspecified`; `AstroUtil.GetJulianDate` then calls `ToUniversalTime()`, which
treats it as **local** time — so an MPC epoch shifts by the machine's UTC offset. Today
this is benign, because for the comet `(q, tp)` parametrization `Epoch_jd` only back-fills
`M_MeanAnomalyAtEpoch` and algebraically cancels in `Kepler.cs:296-310`. It becomes a real
bug the moment an MPC source supplies `a`/`M`.

Compute the JD directly instead of round-tripping through `DateTime`, reusing the
`NOVAS.JulianDate(year, month, day, hour)` call already used for `tp_PeriapsisTime_jd` two
lines below in the same method.

---

## Tests

New `Tests/Calculations/SolarSystemBodyEphemerisTests.cs`, modeled on the existing
`Tests/Calculations/AsteroidEphemerisTests.cs` (same `[TestFixture, Category("RequiresSqlite")]`
setup, same Greenwich observer constants, same "REF: JPL Horizons ..." comment convention):

- Moon, Sun, Venus, Mars, Jupiter, Saturn at 2026-08-19 00:00 UT from Greenwich, asserted
  against Horizons topocentric ICRF. Measured residual is ≤0.04″; assert at **5″** for
  ephemeris-version headroom. The Moon case alone is a 48′ regression guard.
- The Moon at several hour angles across a night, so a future geocentric regression cannot
  hide at one particular hour.
- A Moon tracking-rate case asserting RA/Dec drift rates against the Horizons topocentric
  rate of motion (~2090″/hr RA, ~-431″/hr Dec), which the geocentric implementation misses
  by -239″/-176″ per hour.

Reference values already captured (Horizons topocentric ICRF, Greenwich
lon -0.0014 / lat 51.4769 / alt 46 m, 2026-08-19 00:00 UT):

| Body | RA (deg) | Dec (deg) |
|---|---|---|
| Moon | 218.536943971 | -21.229102197 |
| Mars | 95.088168534 | 23.695793993 |
| Jupiter | 133.144328157 | 18.076134344 |
| Venus | 189.855860130 | -6.222824713 |
| Saturn | 13.822899736 | 3.088962996 |
| Sun | 147.945861420 | 12.953849817 |

Also add an Apophis case to `AsteroidEphemerisTests.cs` — a NEO at 0.0135 au where diurnal
parallax is 646″ — so the asteroid path's topocentric correctness is pinned too. Elements
and reference values are in the Context section above; expected residual 0.74″ after the
ΔT fix, so the suite's existing 30″ tolerance is ample.

Tighten `AsteroidEphemerisTests` / `CometEphemerisTests` tolerance only if all cases land
well inside it after the ΔT fix; otherwise leave at 30″.

## Verification

1. `dotnet build` the plugin and `dotnet test` from the repo root. Baseline before changes
   is **280/280 passing**; after the change it is **296/296**. No existing test should break. `OrbitalElementsAccessorTests.cs:113`
   (`GetSolarSystemBodyPV_Jupiter_...`) and the `OrbitalFramingWizardExportTests` call sites
   need signature updates.
2. The new solar-system suite should pass at 5″, with the Moon well inside it.
3. Sanity-check in the UI: load the Moon in the Orbitals panel and confirm the displayed
   RA/Dec now matches Stellarium's **J2000** readout for the same instant and site, and
   that the reported distance matches Horizons' topocentric `delta` rather than the
   geocentric value.
4. Per `CLAUDE.md`, copy this plan into `plans/` and commit it with the implementation.
