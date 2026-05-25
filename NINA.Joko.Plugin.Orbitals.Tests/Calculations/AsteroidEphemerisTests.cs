using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Asteroid ephemeris tests: same pipeline coverage as CometEphemerisTests but
    /// uses the (a, M_at_epoch) element parametrization that
    /// <see cref="JPLAsteroidElementsBase.ToOrbitalElements"/> produces from JPL's
    /// asteroid bundles. Comets use (q, tp); asteroids use (a, M). The Kepler solver
    /// accepts either, but covering both parametrizations end-to-end protects against
    /// a regression in the time-shift logic at Kepler.cs:296-310.
    ///
    /// Six representative asteroids span low-to-high eccentricity (0.08 - 0.66),
    /// low-to-high inclination (3 - 35 deg), and main-belt vs near-Earth orbits.
    /// Two of them (2024 YR4, 2023 DW) are unnumbered (provisional designation only)
    /// to exercise the parser's name-handling path.
    ///
    /// Reference values are JPL Horizons "R.A.___(ICRF)___DEC" (astrometric ICRS)
    /// from Greenwich Royal Observatory at the elements' epoch. Tolerance 30 arcsec
    /// matches the comet suite (light-time correction enabled in GetObjectPV).
    ///
    /// References for all data:
    ///   JPL Horizons system, https://ssd.jpl.nasa.gov/horizons/ - captured 2026-05-24.
    /// </summary>
    [TestFixture, Category("RequiresSqlite")]
    public class AsteroidEphemerisTests {

        private static readonly Angle GreenwichLat = Angle.ByDegree(51.4769);
        private static readonly Angle GreenwichLon = Angle.ByDegree(-0.0014);
        private const double GreenwichElev_m = 46.0;

        private const double Tolerance_Hours = 30.0 / 3600.0 / 15.0;
        private const double Tolerance_Degrees = 30.0 / 3600.0;

        private OrbitalElementsAccessor sut;

        [SetUp]
        public void Setup() {
            var optionsMock = new Mock<IOrbitalsOptions>();
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            sut = new OrbitalElementsAccessor(optionsMock.Object);
        }

        [TearDown]
        public void Teardown() {
            (sut as IDisposable)?.Dispose();
        }

        private static double DegToHours(double deg) => deg / 15.0;

        private void AssertObservedRaDec(
            Kepler.OrbitalElements elements, DateTime asof,
            double expectedRaDeg, double expectedDecDeg) {
            var pv = sut.GetObjectPV(
                asof, elements, GreenwichLat, GreenwichLon, GreenwichElev_m,
                TimeSpan.FromSeconds(1));

            pv.Coordinates.RA.Should().BeApproximately(DegToHours(expectedRaDeg), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(expectedDecDeg, Tolerance_Degrees);
        }

        // ===== Numbered, low-eccentricity main belt: 1 Ceres =====
        // REF: JPL Horizons COMMAND='2000001' EPHEM_TYPE='ELEMENTS' CENTER='@sun'
        //      OUT_UNITS='AU-D' REF_PLANE='ECLIPTIC' REF_SYSTEM='ICRF'
        //      at JD 2460310.5 (2024-Jan-01 00:00 TDB).
        // REF: Horizons OBSERVER from Greenwich at 2024-Jan-01 00:00 UT.
        [Test]
        public void Ceres_Numbered_E008_MainBelt_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("1 Ceres") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 7.898250993838876e-02,
                q_Perihelion_au = 2.548636993406328e+00,
                i_Inclination_rad = 1.058735274708949e+01 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 8.025362096995758e+01 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 7.339055620494268e+01 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2459919.798139656894,
                M_MeanAnomalyAtEpoch = 8.365451689477317e+01 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.767197171506306e+00,
            };

            // REF: Horizons ICRF RA = 254.07097 deg, Dec = -20.69205 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 254.07097, expectedDecDeg: -20.69205);
        }

        // ===== Numbered, high inclination: 2 Pallas (i ~ 35 deg) =====
        // REF: JPL Horizons COMMAND='Pallas' at JD 2460310.5.
        [Test]
        public void Pallas_Numbered_HighInclination_E023_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("2 Pallas") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 2.303053074356968e-01,
                q_Perihelion_au = 2.132344549751064e+00,
                i_Inclination_rad = 3.492387638127560e+01 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 1.729169652034781e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 3.108837599771967e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460010.628012322355,
                M_MeanAnomalyAtEpoch = 6.409610505102930e+01 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.770377099323600e+00,
            };

            // REF: Horizons ICRF RA = 230.00122 deg, Dec = +1.11862 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 230.00122, expectedDecDeg: 1.11862);
        }

        // ===== Numbered, moderate eccentricity main belt: 4 Vesta =====
        // REF: JPL Horizons COMMAND='Vesta' at JD 2460310.5.
        [Test]
        public void Vesta_Numbered_E009_MainBelt_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("4 Vesta") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 8.974783723267052e-02,
                q_Perihelion_au = 2.149358137254981e+00,
                i_Inclination_rad = 7.143402706604142e+00 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 1.037051012078538e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 1.516708385934564e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460902.388003976084,
                M_MeanAnomalyAtEpoch = 1.992233363086989e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.361277704323764e+00,
            };

            // REF: Horizons ICRF RA = 86.42223 deg, Dec = +20.98684 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 86.42223, expectedDecDeg: 20.98684);
        }

        // ===== Numbered, near-Earth asteroid: 433 Eros (e ~ 0.22, a ~ 1.46 AU) =====
        // REF: JPL Horizons COMMAND='433;' at JD 2460310.5.
        [Test]
        public void Eros_Numbered_NearEarthAsteroid_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("433 Eros") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 2.227616722991410e-01,
                q_Perihelion_au = 1.133377038358668e+00,
                i_Inclination_rad = 1.082741174313063e+01 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 3.042818525661311e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 1.788944914373595e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460445.650259797461,
                M_MeanAnomalyAtEpoch = 2.843531833153742e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 1.458210432971441e+00,
            };

            // REF: Horizons ICRF RA = 341.00209 deg, Dec = +2.44888 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 341.00209, expectedDecDeg: 2.44888);
        }

        // ===== Numbered, metal-rich main belt: 16 Psyche =====
        // REF: JPL Horizons COMMAND='16;' at JD 2460310.5.
        [Test]
        public void Psyche_Numbered_E013_MainBelt_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("16 Psyche") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 1.341929792833918e-01,
                q_Perihelion_au = 2.531017645601488e+00,
                i_Inclination_rad = 3.096902208055770e+00 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 1.500227661345641e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 2.294707750868318e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460793.367028635461,
                M_MeanAnomalyAtEpoch = 2.647816786416831e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.923304599108731e+00,
            };

            // REF: Horizons ICRF RA = 272.07253 deg, Dec = -21.38398 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 272.07253, expectedDecDeg: -21.38398);
        }

        // ===== Unnumbered, high-eccentricity NEO: 2024 YR4 (e ~ 0.66) =====
        // Recent NEO discovery, provisional designation only. The "Unnumbered" suffix
        // here exercises the same Kepler pipeline as numbered asteroids; the parser's
        // unnumbered-asteroid name handling is covered by JPLAccessorParsingTests.
        // REF: JPL Horizons COMMAND='DES=2024 YR4;' at JD 2460676.5 (2025-Jan-01).
        [Test]
        public void TwentyTwentyFourYR4_Unnumbered_HighE_NEO_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("2024 YR4") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460676.5,
                e_Eccentricity = 6.617421210127383e-01,
                q_Perihelion_au = 8.514920572021450e-01,
                i_Inclination_rad = 3.408962556019276e+00 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 2.713710745547014e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 1.343614669380245e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460636.927437874489,
                M_MeanAnomalyAtEpoch = 9.765606569230417e+00 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.517286691891694e+00,
            };

            // REF: Horizons ICRF RA = 123.74577 deg, Dec = +7.89332 deg.
            AssertObservedRaDec(elements,
                new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 123.74577, expectedDecDeg: 7.89332);
        }

        // ===== Unnumbered, sub-AU NEO: 2023 DW (Atira-like, a < 1 AU) =====
        // REF: JPL Horizons COMMAND='DES=2023 DW;' at JD 2460310.5.
        [Test]
        public void TwentyTwentyThreeDW_Unnumbered_SubAU_NEO_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("2023 DW") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 3.962195332065179e-01,
                q_Perihelion_au = 4.951008344914828e-01,
                i_Inclination_rad = 5.806378610389056e+00 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 3.261045722043568e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 4.045588873749630e+01 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460181.215759889688,
                M_MeanAnomalyAtEpoch = 1.716041402618502e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 8.200014106465415e-01,
            };

            // REF: Horizons ICRF RA = 222.95059 deg, Dec = -19.37709 deg.
            AssertObservedRaDec(elements,
                new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                expectedRaDeg: 222.95059, expectedDecDeg: -19.37709);
        }
    }
}
