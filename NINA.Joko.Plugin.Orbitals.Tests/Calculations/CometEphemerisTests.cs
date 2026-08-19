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
    /// End-to-end tests covering the full elements -> Kepler propagation -> topocentric
    /// RA/Dec pipeline (Kepler.CalculateOrbitalElements + Kepler.GetTopocentricJ2000Position
    /// + NOVAS ecliptic-to-equatorial rotation), exercised across the full range of
    /// orbital eccentricities using real comet ephemerides from JPL Horizons.
    ///
    /// Each case cites the exact Horizons elements query and the exact Horizons
    /// observer-ephemeris query used to produce the reference RA/Dec values. The
    /// observer is Greenwich Royal Observatory (lat 51.4769 N, lon -0.0014 E,
    /// elev 46 m). The requested instant matches the elements' reference epoch so
    /// propagation time is essentially zero.
    ///
    /// Reference values used here are Horizons' "R.A.___(ICRF)___DEC" column, i.e.
    /// the astrometric ICRS position. The plugin's GetTopocentricJ2000Position resolves
    /// the vector in the J2000 mean equatorial frame and does NOT add the
    /// precession-to-date, nutation, annual aberration, or light-deflection
    /// corrections that Horizons' "a-appar" column carries -- so comparing against
    /// ICRS is the apples-to-apples check. The systematic offset between ICRS and
    /// apparent for these dates is ~17-22 arcmin, which would otherwise swamp the
    /// test signal.
    ///
    /// Tolerance is set at ~5 arcmin to absorb the remaining residuals from
    /// Kepler-only propagation (vs. Horizons' n-body model), the UTC/TT
    /// difference (~69s in 2024 -> sub-arcmin motion for slow objects),
    /// and light-time omission (the plugin reports instantaneous geocentric vectors
    /// at the requested epoch; Horizons reports the position observed at the
    /// requested epoch, which is the actual position at t - light_time).
    ///
    /// References for all data:
    ///   JPL Horizons system, https://ssd.jpl.nasa.gov/horizons/ - captured 2026-05-24.
    /// </summary>
    [TestFixture, Category("RequiresSqlite")]
    public class CometEphemerisTests {

        // Greenwich Royal Observatory.
        // REF: longitude convention is positive-east (matches JPL Horizons SITE_COORD).
        private static readonly Angle GreenwichLat = Angle.ByDegree(51.4769);
        private static readonly Angle GreenwichLon = Angle.ByDegree(-0.0014);
        private const double GreenwichElev_m = 46.0;

        // 30 arcsec, made possible by the light-time correction in GetObjectPV.
        // Prior to that fix the tolerance was 5 arcmin (10x looser) to absorb the
        // light-time error. Residuals against Horizons ICRS are now ~1-3 arcsec
        // dominated by two-body vs n-body propagation drift.
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

        /// <summary>
        /// Convenience: convert Horizons-style degrees-RA to NINA-style hours-RA.
        /// </summary>
        private static double DegToHours(double deg) => deg / 15.0;

        // ===== Elliptic, highly eccentric: 1P/Halley (e ~= 0.967) =====
        //
        // REF: JPL Horizons heliocentric ecliptic ICRF elements, record 90000030
        //   (Halley's 1968 fit -- the most recent SBDB entry for 1P), at the
        //   epoch JD 2460310.5 (2024-Jan-01 00:00 TDB):
        //     https://ssd.jpl.nasa.gov/api/horizons.api
        //     ?COMMAND='90000030' EPHEM_TYPE='ELEMENTS' CENTER='@sun'
        //     START_TIME='2024-01-01' STOP_TIME='2024-01-02' STEP_SIZE='1d'
        //     OUT_UNITS='AU-D' REF_PLANE='ECLIPTIC' REF_SYSTEM='ICRF'
        //
        // REF: JPL Horizons apparent RA/Dec (airless) from Greenwich at the same
        //   instant (2024-Jan-01 00:00 UT):
        //     CENTER='coord@399' COORD_TYPE='GEODETIC'
        //     SITE_COORD='-0.0014,51.4769,0.046' QUANTITIES='1,2' ANG_FORMAT='DEG'
        [Test]
        public void Halley_Elliptic_E0967_NearAphelion_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("1P/Halley") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 9.671963238292548e-01,
                q_Perihelion_au = 5.860293714380836e-01,
                i_Inclination_rad = 1.621432945055577e+02 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 5.963182256611607e+01 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 1.125368242714493e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2474077.017389177345,
                M_MeanAnomalyAtEpoch = 1.803062959803052e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 1.786474687738544e+01,
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 125.02370 deg, Dec = +2.20985 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(125.02370), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(2.20985, Tolerance_Degrees);
        }

        // ===== Elliptic, moderate: 2P/Encke (e ~= 0.847) =====
        //
        // REF: JPL Horizons record 90000091 (Encke 2022 fit), epoch JD 2460310.5:
        //     CENTER='@sun' EPHEM_TYPE='ELEMENTS' START_TIME='2024-01-01'
        // REF: Horizons observer ephemeris from Greenwich, 2024-Jan-01 00:00 UT.
        [Test]
        public void Encke_Elliptic_E085_PostPerihelion_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("2P/Encke") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 8.469402221757967e-01,
                q_Perihelion_au = 3.395903560937322e-01,
                i_Inclination_rad = 1.133660145697455e+01 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 3.340177875220353e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 1.872885225255060e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460240.028459810186,
                M_MeanAnomalyAtEpoch = 2.101727624638819e+01 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 2.218677963088178e+00,
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 289.80879 deg, Dec = -26.67592 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(289.80879), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(-26.67592, Tolerance_Degrees);
        }

        // ===== Elliptic, low-eccentricity short-period: 67P/Churyumov-Gerasimenko (e ~= 0.65) =====
        //
        // REF: JPL Horizons record 90000702 (67P 2015 fit), epoch JD 2460310.5.
        // REF: Horizons observer ephemeris from Greenwich, 2024-Jan-01 00:00 UT.
        [Test]
        public void Sixty67P_Elliptic_E065_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("67P/Churyumov-Gerasimenko") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460310.5,
                e_Eccentricity = 6.500002994560767e-01,
                q_Perihelion_au = 1.210285248234158e+00,
                i_Inclination_rad = 3.871430446701042e+00 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 3.633048079337558e+01 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 2.216446364926212e+01 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2459520.837344826665,
                M_MeanAnomalyAtEpoch = 1.210362815918402e+02 * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = 3.457960810690103e+00,
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 226.84827 deg, Dec = -17.85431 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(226.84827), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(-17.85431, Tolerance_Degrees);
        }

        // ===== Near-parabolic: C/2023 A3 (Tsuchinshan-ATLAS), e = 1.00002 =====
        //
        // Tsuchinshan-ATLAS's eccentricity at this epoch is 1.00002, technically
        // hyperbolic but indistinguishable from parabolic for practical observation.
        // The Kepler.cs branch chooser uses |e-1| < double.Epsilon to detect the
        // parabolic case, so this comet flows through the hyperbolic branch.
        //
        // REF: JPL Horizons DES='C/2023 A3', epoch JD 2460580.5 (perihelion was at
        //   Tp=2460581.242 -- this snapshot is ~0.7 days before perihelion).
        // REF: Horizons observer ephemeris from Greenwich, 2024-Sep-27 00:00 UT.
        [Test]
        public void TsuchinshanAtlas_NearParabolic_E1_00002_AtPerihelion_ApparentRaDecFromGreenwich() {
            // For e > 1 with negative Horizons-style A, omit a_SemiMajorAxis_au and
            // let Kepler.cs derive it from q+e (which flips the sign per line 280).
            var elements = new Kepler.OrbitalElements("C/2023 A3 (Tsuchinshan-ATLAS)") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2460580.5,
                e_Eccentricity = 1.000019935532537e+00,
                q_Perihelion_au = 3.914228752245780e-01,
                i_Inclination_rad = 1.391105463439625e+02 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 2.155945664798743e+01 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 3.084913095038683e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2460581.242084843572,
                // a deliberately omitted -- derived from q + e by Kepler.cs:278-282.
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2024, 9, 27, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 161.09839 deg, Dec = -6.09421 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(161.09839), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(-6.09421, Tolerance_Degrees);
        }

        // ===== Hyperbolic: 1I/'Oumuamua (e = 1.201) =====
        //
        // First confirmed interstellar object. Pre-perihelion (perihelion was
        // 2017-Sep-09); 2017-Nov-01 is ~53 days post-perihelion and the object
        // is moving fast as it leaves the inner solar system.
        //
        // REF: JPL Horizons DES='C/2017 U1' = 1I/'Oumuamua, epoch JD 2458058.5.
        // REF: Horizons observer ephemeris from Greenwich, 2017-Nov-01 00:00 UT.
        [Test]
        public void Oumuamua_Hyperbolic_E12_PostPerihelion_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("1I/'Oumuamua") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2458058.5,
                e_Eccentricity = 1.201062295583522e+00,
                q_Perihelion_au = 2.559157223972130e-01,
                i_Inclination_rad = 1.227410653987060e+02 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 2.459682795984680e+01 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 2.418078602792230e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2458006.003519309685,
                M_MeanAnomalyAtEpoch = 3.603170114842506e+01 * AstrometricConstants.RAD_PER_DEG,
                // a omitted -- derived (Horizons gave A=-1.273 AU, the convention flip
                // at Kepler.cs:280 stores it as +1.273).
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2017, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 354.69183 deg, Dec = +5.42270 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(354.69183), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(5.42270, Tolerance_Degrees);
        }

        // ===== Strongly hyperbolic: 2I/Borisov (e = 3.357) =====
        //
        // Second confirmed interstellar object. Pre-perihelion at this epoch
        // (perihelion was 2019-Dec-08, this snapshot is 7 days earlier),
        // so the mean anomaly is negative.
        //
        // REF: JPL Horizons DES='C/2019 Q4' = 2I/Borisov, epoch JD 2458818.5.
        // REF: Horizons observer ephemeris from Greenwich, 2019-Dec-01 00:00 UT.
        [Test]
        public void Borisov_StronglyHyperbolic_E336_PrePerihelion_ApparentRaDecFromGreenwich() {
            var elements = new Kepler.OrbitalElements("2I/Borisov") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2458818.5,
                e_Eccentricity = 3.356481235327800e+00,
                q_Perihelion_au = 2.006522980449558e+00,
                i_Inclination_rad = 4.405260008314845e+01 * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = 3.081476898904831e+02 * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = 2.091244949827712e+02 * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = 2458826.053916433826,
                M_MeanAnomalyAtEpoch = -9.475584779934140e+00 * AstrometricConstants.RAD_PER_DEG,
                // a omitted; Horizons A=-0.8515, flipped to +0.8515 by Kepler.cs.
            };

            var pv = sut.GetObjectPV(
                asof: new DateTime(2019, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 168.90202 deg, Dec = -12.56277 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(168.90202), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(-12.56277, Tolerance_Degrees);
        }

        // ===== Synthetic parabolic: e = 1.0 exactly =====
        //
        // No real comet has e == 1.0 to double-precision exactness, and Kepler.cs's
        // parabolic-branch chooser uses `Math.Abs(ecc - 1.0d) < double.Epsilon`
        // (essentially exact equality). To exercise Barker's-equation branch we use
        // a synthetic orbit: q = 1 AU, perihelion at JD 2451545.0 (J2000.0),
        // observed 100 days later. The expected position is derived from Barker's
        // identity itself (independent of the algorithm's intermediate steps):
        //
        //   W = (3/2) * sqrt(mu / (2 q^3)) * (t - tp)
        //   tan^3(v/2) + 3 tan(v/2) = 2 W                        (Barker's equation)
        //   r = 2 q / (1 + cos(v))                                (parabolic geometry)
        //
        // We solve Barker numerically with Newton's method (independent of Kepler.cs),
        // then assert that the OrbitalPosition the plugin returns matches the
        // expected v0 and Distance.AU.
        //
        // REF: Bate, Mueller, White, "Fundamentals of Astrodynamics," ch. 4, eq. 4.2-15;
        //      also Meeus "Astronomical Algorithms" ch. 35.
        [Test]
        public void SyntheticParabolic_E1Exact_PropagationSatisfiesBarkerIdentity() {
            const double q_au = 1.0;
            const double tp_jd = 2451545.0;
            const double daysSincePerihelion = 100.0;
            var asofJd = tp_jd + daysSincePerihelion;

            var elements = new Kepler.OrbitalElements("synthetic-parabolic") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = tp_jd,
                q_Perihelion_au = q_au,
                e_Eccentricity = 1.0,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                tp_PeriapsisTime_jd = tp_jd,
            };

            var actual = Kepler.CalculateOrbitalElements(elements, asofJd);

            // Independent computation of expected v via Barker's equation,
            // using Newton's method to solve  D^3 + 3D - 2W = 0  where D = tan(v/2).
            var mu = Kepler.GravitationalParameter.Sun.Parameter_au3_d2;
            var w = 1.5 * Math.Sqrt(mu / (2.0 * q_au * q_au * q_au)) * daysSincePerihelion;
            var d = NewtonSolveCubic(w);
            var expectedV = 2.0 * Math.Atan(d);
            var expectedR = 2.0 * q_au / (1.0 + Math.Cos(expectedV));

            actual.v0_TrueAnomaly_rad.Should().BeApproximately(expectedV, 1e-9);
            actual.Distance.AU.Should().BeApproximately(expectedR, 1e-9);

            // Sanity: the parabolic geometric identity holds for any (v, r) on the orbit.
            var identityR = 2.0 * q_au / (1.0 + Math.Cos(actual.v0_TrueAnomaly_rad));
            actual.Distance.AU.Should().BeApproximately(identityR, 1e-9,
                "parabolic identity r = 2q/(1+cos(v)) must hold");
        }

        [Test]
        public void SyntheticParabolic_AtPerihelion_DistanceEqualsQ_TrueAnomalyZero() {
            const double q_au = 1.0;
            const double tp_jd = 2451545.0;
            var elements = new Kepler.OrbitalElements("synthetic-parabolic-at-perihelion") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = tp_jd,
                q_Perihelion_au = q_au,
                e_Eccentricity = 1.0,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                tp_PeriapsisTime_jd = tp_jd,
            };

            var actual = Kepler.CalculateOrbitalElements(elements, tp_jd);

            actual.v0_TrueAnomaly_rad.Should().BeApproximately(0.0, 1e-12);
            actual.Distance.AU.Should().BeApproximately(q_au, 1e-12);
        }

        // ===== Element-source comparison: 220P/McNaught from the two production bundles =====
        //
        // Every other case in this file feeds the solver elements taken at (or very near)
        // the observation epoch, which isolates the propagation maths. This pair instead
        // feeds it the *actual rows the plugin downloads*, to check that the shipped data
        // sources are fit for purpose. They are not equally fit.
        //
        // JPL publishes each comet's osculating elements at that orbit solution's own
        // reference epoch, not at a common current epoch. For 220P that is MJD 59114
        // (2020-Sep-22), so the plugin two-body propagates across ~6 years and a full
        // revolution. MPC republishes every comet at a single current epoch.
        //
        // Measured 2026-08-19 against Horizons (DES=220P; CAP<2026-08-19;), topocentric
        // ICRF from Greenwich:
        //     JPL row  -> 5067" (84.5') off
        //     MPC row  -> 0.41" off
        // The divergence is almost entirely perihelion timing: propagating JPL's
        // tp = 2459194.14096 forward one revolution lands ~2.0 days before MPC's
        // tp = 2461205.6177 (Horizons' own osculating fit says 2461205.61863, i.e. MPC is
        // right to ~80 seconds).
        //
        // Staleness in the JPL bundle is systemic rather than specific to this comet: of
        // its 3841 entries, only 1.4% carry an epoch less than a year old and 78.6% are
        // more than a decade stale. MPC's bundle has 946 of 949 entries at one current
        // epoch. IOrbitalsOptions.CometAccessor already defaults to MPC, so this is a
        // trap only for users who switch the source to JPL.
        //
        // Only the MPC path is asserted. Asserting the JPL error would encode a defect as
        // expected behaviour and would start failing if JPL ever re-anchors the file.

        // REF: MPC CometEls.txt row, downloaded 2026-08-19:
        // 0220P         2026 06 14.1177  1.559302  0.500331  180.5619  150.0839    8.1208  20260819 ...
        [Test]
        public void McNaught220P_FromMpcBundle_ApparentRaDecFromGreenwich() {
            var elements = new MPCCometElements() {
                name = "220P/McNaught",
                number = 220,
                tpYear = 2026,
                tpMonth = 6,
                tpDay_tt = 14.1177,
                perihelionDistance_au = 1.559302,
                eccentricity = 0.500331,
                argOfPerihelion_deg = 180.5619,
                longOfAscendingNode_deg = 150.0839,
                incAscendingNode_deg = 8.1208,
                epoch = new DateTime(2026, 8, 19)
            }.ToOrbitalElements();

            var pv = sut.GetObjectPV(
                asof: new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                orbitalElements: elements,
                latitude: GreenwichLat, longitude: GreenwichLon, elevation: GreenwichElev_m,
                rateDriftDelta: TimeSpan.FromSeconds(1));

            // REF: Horizons ICRF (astrometric) RA = 45.305221 deg, Dec = +9.484476 deg.
            pv.Coordinates.RA.Should().BeApproximately(DegToHours(45.305221), Tolerance_Hours);
            pv.Coordinates.Dec.Should().BeApproximately(9.484476, Tolerance_Degrees);
        }

        /// <summary>
        /// Guards the MPC epoch parse. MPC epochs are TT at 0h, and the record is read with
        /// DateTime.ParseExact, which yields DateTimeKind.Unspecified -- routing that through
        /// AstroUtil.GetJulianDate would call ToUniversalTime() and shift the epoch by the
        /// machine's UTC offset. Epoch_jd must be the plain calendar julian date regardless
        /// of the test machine's time zone.
        /// </summary>
        [Test]
        public void McNaught220P_FromMpcBundle_EpochIsTimeZoneIndependent() {
            var elements = new MPCCometElements() {
                name = "220P/McNaught",
                tpYear = 2026,
                tpMonth = 6,
                tpDay_tt = 14.1177,
                perihelionDistance_au = 1.559302,
                eccentricity = 0.500331,
                argOfPerihelion_deg = 180.5619,
                longOfAscendingNode_deg = 150.0839,
                incAscendingNode_deg = 8.1208,
                epoch = new DateTime(2026, 8, 19)
            }.ToOrbitalElements();

            elements.Epoch_jd.Should().Be(2461271.5);
            elements.tp_PeriapsisTime_jd.Should().BeApproximately(2461205.6177, 1e-4);
        }

        /// <summary>
        /// Newton's method on the cubic D^3 + 3D - 2W = 0. The closed-form solution
        /// is the classic Cardano expression, but Newton converges in a handful of
        /// iterations from initial guess D0 = (2W)^(1/3) and avoids the implementation's
        /// own algebraic path -- which is the whole point of an independent check.
        /// </summary>
        private static double NewtonSolveCubic(double w) {
            var d = Math.Cbrt(2.0 * w);
            for (var i = 0; i < 50; i++) {
                var f = d * d * d + 3.0 * d - 2.0 * w;
                var fprime = 3.0 * d * d + 3.0;
                var delta = f / fprime;
                d -= delta;
                if (Math.Abs(delta) < 1e-15) break;
            }
            return d;
        }
    }
}
