using NINA.Joko.Plugin.Orbitals.Calculations;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.TestHelpers {

    /// <summary>
    /// Well-known orbital element sets, each citing the source from which the
    /// numbers were taken. Tests should prefer these over inlining magic numbers
    /// so the provenance is visible.
    /// </summary>
    internal static class Fixtures {

        // REF: JPL Small-Body Database, 1P/Halley, epoch JD 2449400.5 (1994-Feb-17.0 TDB).
        // https://ssd.jpl.nasa.gov/tools/sbdb_lookup.html#/?sstr=1P
        // Captured 2024. Comet elements are heliocentric, ecliptic, J2000.
        public static Kepler.OrbitalElements Halley() {
            return new Kepler.OrbitalElements("1P/Halley") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2449400.5,
                q_Perihelion_au = 0.5859781115,
                e_Eccentricity = 0.9671429085,
                i_Inclination_rad = 162.262690579161 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 111.3324851045177 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 58.42008097656843 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2446470.95154,
                a_SemiMajorAxis_au = 17.834144,
            };
        }

        // REF: JPL Small-Body Database, 1 Ceres, epoch JD 2460200.5 (2023-Sep-13.0 TDB).
        // https://ssd.jpl.nasa.gov/tools/sbdb_lookup.html#/?sstr=Ceres
        // Captured 2024. Asteroid elements are heliocentric, ecliptic, J2000.
        public static Kepler.OrbitalElements Ceres() {
            return new Kepler.OrbitalElements("1 Ceres") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460200.5,
                a_SemiMajorAxis_au = 2.7691652,
                e_Eccentricity = 0.0789126,
                i_Inclination_rad = 10.5879572 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 73.4322886 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 80.2549325 * AstrometricConstants.RAD_PER_DEG,
                M_MeanAnomalyAtEpoch = 60.0728817 * AstrometricConstants.RAD_PER_DEG,
            };
        }

        /// <summary>
        /// Synthetic circular orbit: a = 1 AU, e = 0, i = 0, periapsis at the
        /// reference direction. Useful for sanity-checking that v == M when e = 0.
        /// </summary>
        public static Kepler.OrbitalElements CircularSyntheticOrbit(double epochJd = 2451545.0) {
            return new Kepler.OrbitalElements("circular-synthetic") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = epochJd,
                a_SemiMajorAxis_au = 1.0,
                e_Eccentricity = 0.0,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                M_MeanAnomalyAtEpoch = 0.0,
            };
        }

        /// <summary>
        /// Synthetic hyperbolic orbit: e = 1.5. The semi-major axis sign / magnitude
        /// here matches Kepler.cs's convention (positive after the line 280 flip when
        /// derived from q).
        /// </summary>
        public static Kepler.OrbitalElements HyperbolicSyntheticOrbit(double epochJd = 2451545.0) {
            return new Kepler.OrbitalElements("hyperbolic-synthetic") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = epochJd,
                q_Perihelion_au = 1.0,
                e_Eccentricity = 1.5,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                tp_PeriapsisTime_jd = epochJd,
            };
        }
    }
}
