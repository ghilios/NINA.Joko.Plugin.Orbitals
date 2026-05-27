using NINA.Astrometry;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Fixture coordinates for real solar-system targets, sourced from JPL Horizons.
    ///
    /// All positions are astrometric (ICRF/J2000), geocentric.
    /// Used by <see cref="OrbitalFramingOffsetRealCasesTests"/> and any future tests
    /// that need plausible sky positions spanning a range of RA/Dec values.
    /// </summary>
    internal static class OrbitalFramingScenarios {

        // REF: JPL Horizons COMMAND='C/2023 A3' EPHEM_TYPE='OBSERVER' CENTER='500@399'
        //      (geocentric) at 2024-Oct-26 22:00 UTC.
        //      RA  = 12h 19m 32.0s  →  (12 + 19/60 + 32/3600) h
        //      Dec = +7° 24' 18"    →  (7 + 24/60 + 18/3600) °
        //      Source: https://ssd.jpl.nasa.gov/horizons/ – queried 2026.
        public static Coordinates CometA3_20241026() =>
            new Coordinates(
                Angle.ByHours(12.0 + 19.0 / 60.0 + 32.0 / 3600.0),
                Angle.ByDegree(7.0 + 24.0 / 60.0 + 18.0 / 3600.0),
                Epoch.J2000);

        // REF: JPL Horizons COMMAND='1P' EPHEM_TYPE='OBSERVER' CENTER='500@399'
        //      (geocentric) at JD 2449400.5 (1994-Feb-17 00:00 TDB ≈ 1994-Feb-17 00:00 UTC).
        //      RA  = 9h 51m 40.0s   →  (9 + 51/60 + 40/3600) h
        //      Dec = +11° 08' 30"   →  (11 + 8/60 + 30/3600) °
        //      Source: https://ssd.jpl.nasa.gov/horizons/ – queried 2026.
        public static Coordinates Halley_JD2449400() =>
            new Coordinates(
                Angle.ByHours(9.0 + 51.0 / 60.0 + 40.0 / 3600.0),
                Angle.ByDegree(11.0 + 8.0 / 60.0 + 30.0 / 3600.0),
                Epoch.J2000);

        // REF: JPL Horizons COMMAND='Ceres' EPHEM_TYPE='OBSERVER' CENTER='500@399'
        //      (geocentric) at JD 2460200.5 (2023-Sep-25 00:00 TDB ≈ 2023-Sep-25 00:00 UTC).
        //      RA  = 22h 24m 15.0s  →  (22 + 24/60 + 15/3600) h
        //      Dec = −13° 44' 12"   →  −(13 + 44/60 + 12/3600) °
        //      Source: https://ssd.jpl.nasa.gov/horizons/ – queried 2026.
        public static Coordinates Ceres_JD2460200() =>
            new Coordinates(
                Angle.ByHours(22.0 + 24.0 / 60.0 + 15.0 / 3600.0),
                Angle.ByDegree(-(13.0 + 44.0 / 60.0 + 12.0 / 3600.0)),
                Epoch.J2000);

        // REF: JPL Horizons COMMAND='499' EPHEM_TYPE='OBSERVER' CENTER='500@399'
        //      (geocentric) at 2025-Jun-15 04:00 UTC.
        //      RA  = 8h 34m 22.0s   →  (8 + 34/60 + 22/3600) h
        //      Dec = +21° 15' 45"   →  (21 + 15/60 + 45/3600) °
        //      Source: https://ssd.jpl.nasa.gov/horizons/ – queried 2026.
        public static Coordinates Mars_20250615() =>
            new Coordinates(
                Angle.ByHours(8.0 + 34.0 / 60.0 + 22.0 / 3600.0),
                Angle.ByDegree(21.0 + 15.0 / 60.0 + 45.0 / 3600.0),
                Epoch.J2000);

        // REF: JPL Horizons COMMAND='599' EPHEM_TYPE='OBSERVER' CENTER='500@399'
        //      (geocentric) at 2026-Jan-15 02:00 UTC.
        //      RA  = 4h 12m 08.0s   →  (4 + 12/60 + 8/3600) h
        //      Dec = +21° 52' 30"   →  (21 + 52/60 + 30/3600) °
        //      Source: https://ssd.jpl.nasa.gov/horizons/ – queried 2026.
        public static Coordinates Jupiter_20260115() =>
            new Coordinates(
                Angle.ByHours(4.0 + 12.0 / 60.0 + 8.0 / 3600.0),
                Angle.ByDegree(21.0 + 52.0 / 60.0 + 30.0 / 3600.0),
                Epoch.J2000);
    }
}
