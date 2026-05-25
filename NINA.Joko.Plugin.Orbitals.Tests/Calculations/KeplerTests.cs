using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture]
    public class KeplerTests {

        private const double SolverTolerance = 1.0e-11;
        private const double AssertTolerance = 1.0e-9;

        /// <summary>
        /// Builds an OrbitalElements that, when CalculateOrbitalElements is invoked at
        /// asof == epoch, produces the requested mean anomaly verbatim (no time-shift).
        /// Inclination/argument-of-perihelion/node are zero so the ecliptic position
        /// reduces to (r*cos(v), r*sin(v), 0), which makes downstream geometry easy
        /// to verify by hand.
        /// </summary>
        private static Kepler.OrbitalElements MakeAtEpochAt(double meanAnomalyRad, double eccentricity, double semiMajorAxisAu = 1.0) {
            return new Kepler.OrbitalElements("test") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                a_SemiMajorAxis_au = semiMajorAxisAu,
                e_Eccentricity = eccentricity,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                M_MeanAnomalyAtEpoch = meanAnomalyRad,
            };
        }

        // Elliptic Kepler's equation: M = E - e*sin(E).
        // Test cases are derived in the inverse direction: pick a known E and
        // eccentricity, compute M from the definition, then feed M back in
        // and expect the solver to recover E. This avoids any dependency on
        // the solver's own intermediate steps.
        [TestCase(0.0, 0.0, 0.0)]
        [TestCase(1.5707963267948966, 0.0, 1.5707963267948966)]   // M=pi/2, e=0  -> E=pi/2
        [TestCase(Math.PI, 0.3, Math.PI)]                          // E=pi is a fixed point for any e
        [TestCase(0.5792645075960518, 0.5, 1.0)]                   // M = 1 - 0.5*sin(1)
        [TestCase(1.2707963267948966, 0.3, Math.PI / 2)]           // M = pi/2 - 0.3
        [TestCase(1.1816323158568865, 0.9, 2.0)]                   // M = 2 - 0.9*sin(2)
        [TestCase(-0.5792645075960518, 0.5, -1.0)]                 // negative branch, symmetry
        [TestCase(2.872991992740121, 0.9, 3.0)]                    // M = 3 - 0.9*sin(3)
        public void EllipticSolver_RecoversEccentricAnomalyFromMeanAnomaly(double meanAnomaly, double eccentricity, double expectedE) {
            var elements = MakeAtEpochAt(meanAnomaly, eccentricity);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.e_EccentricAnomaly_rad.Should().BeApproximately(expectedE, AssertTolerance);
        }

        [Test]
        public void EllipticSolver_ZeroEccentricity_GivesM_EqualsE_EqualsV() {
            var elements = MakeAtEpochAt(meanAnomalyRad: 0.42, eccentricity: 0.0);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.M_MeanAnomaly_rad.Should().BeApproximately(0.42, AssertTolerance);
            position.e_EccentricAnomaly_rad.Should().BeApproximately(0.42, AssertTolerance);
            position.v0_TrueAnomaly_rad.Should().BeApproximately(0.42, AssertTolerance);
        }

        // Hyperbolic Kepler's equation: M = e*sinh(E) - E.
        // Test cases derived in the same inverse manner as the elliptic suite.
        [TestCase(0.7628017904657021, 1.5, 1.0)]   // M = 1.5*sinh(1) - 1
        [TestCase(5.2537208156940390, 2.0, 2.0)]   // M = 2*sinh(2) - 2
        [TestCase(0.0, 1.5, 0.0)]                  // M=0, E=0
        [TestCase(-0.7628017904657021, 1.5, -1.0)] // negative branch
        public void HyperbolicSolver_RecoversEccentricAnomalyFromMeanAnomaly(double meanAnomaly, double eccentricity, double expectedE) {
            var elements = MakeAtEpochAt(meanAnomaly, eccentricity);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.e_EccentricAnomaly_rad.Should().BeApproximately(expectedE, AssertTolerance);
        }

        [Test]
        public void EllipticDistance_AtPeriapsis_IsAOneMinusE() {
            // E = 0  ->  M = 0  ->  r = a(1 - e*cos(0)) = a(1 - e)
            // REF: standard two-body geometry.
            var elements = MakeAtEpochAt(meanAnomalyRad: 0.0, eccentricity: 0.5, semiMajorAxisAu: 2.0);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.Distance.AU.Should().BeApproximately(2.0 * (1.0 - 0.5), AssertTolerance);
        }

        [Test]
        public void EllipticDistance_AtApoapsis_IsAOnePlusE() {
            // E = pi  ->  M = pi  ->  r = a(1 - e*cos(pi)) = a(1 + e)
            var elements = MakeAtEpochAt(meanAnomalyRad: Math.PI, eccentricity: 0.5, semiMajorAxisAu: 2.0);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.Distance.AU.Should().BeApproximately(2.0 * (1.0 + 0.5), AssertTolerance);
        }

        [Test]
        public void EllipticTrueAnomaly_AtE_EqualsHalfPi_MatchesHalfAngleFormula() {
            // E = pi/2, e = 0.5
            //   tan(v/2) = sqrt((1+e)/(1-e)) * tan(E/2) = sqrt(3) * 1
            //   v = 2*atan(sqrt(3)) = 2*pi/3 = 120 deg
            // Construct from inverse: M = E - e*sin(E) = pi/2 - 0.5
            var meanAnomaly = Math.PI / 2 - 0.5;
            var elements = MakeAtEpochAt(meanAnomalyRad: meanAnomaly, eccentricity: 0.5);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.v0_TrueAnomaly_rad.Should().BeApproximately(2.0 * Math.PI / 3.0, AssertTolerance);
        }

        [Test]
        public void EllipticTrueAnomaly_AtPeriapsis_IsZero() {
            var elements = MakeAtEpochAt(meanAnomalyRad: 0.0, eccentricity: 0.5);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.v0_TrueAnomaly_rad.Should().BeApproximately(0.0, AssertTolerance);
        }

        [Test]
        public void HyperbolicDistance_AtPeriapsis_IsAEMinusOne() {
            // E = 0  ->  M = 0  ->  r = a(e*cosh(0) - 1) = a(e - 1)
            // With the implementation's convention that semi-major axis is stored
            // as a positive scalar for hyperbolic (the negation at Kepler.cs:280 flips
            // the q/(1-e) result back to positive), this gives the expected periapsis.
            // REF: Curtis ch. 3, hyperbolic-orbit geometry.
            var elements = MakeAtEpochAt(meanAnomalyRad: 0.0, eccentricity: 1.5, semiMajorAxisAu: 2.0);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            position.Distance.AU.Should().BeApproximately(2.0 * (1.5 - 1.0), AssertTolerance);
        }

        [Test]
        public void HyperbolicSolver_SemiMajorAxisDerivedFromQ_StoresPositiveValue() {
            // Setting q (and not a) for a hyperbolic orbit should populate a with
            // a positive value (per the negation at Kepler.cs:280), so that downstream
            // distance and n^2 = mu/a^3 are well-defined (no sqrt of a negative).
            var elements = new Kepler.OrbitalElements("test") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                q_Perihelion_au = 1.0,
                e_Eccentricity = 1.5,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                tp_PeriapsisTime_jd = 2451545.0,
            };

            Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            elements.a_SemiMajorAxis_au.Should().NotBeNull();
            elements.a_SemiMajorAxis_au!.Value.Should().BeGreaterThan(0,
                "Kepler.cs:280 flips the sign so |a| is stored");
            // q = 1, e = 1.5  ->  a = q / (e - 1) = 2 (after the sign flip)
            elements.a_SemiMajorAxis_au.Value.Should().BeApproximately(2.0, AssertTolerance);
        }

        [Test, Explicit, Category("BugCandidate")]
        public void OrbitalElements_QFallbackFromA_ComputesPerihelionDistance() {
            // SUSPECTED BUG: Kepler.cs:409 sets q = (1 + e) * a, which is aphelion.
            // Correct: perihelion q = a * (1 - e) for elliptic orbits.
            //
            // This fallback only fires when callers supply 'a' but not 'q'.
            // Most JPL/MPC ingestion paths supply 'q' directly, so live paths
            // usually side-step it -- but anything that does fall through gets
            // the maximum orbital distance written into the perihelion slot.
            var elements = new Kepler.OrbitalElements("test") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                a_SemiMajorAxis_au = 2.0,
                e_Eccentricity = 0.5,
                i_Inclination_rad = 0.0,
                w_ArgOfPerihelion_rad = 0.0,
                node_LongitudeOfAscending_rad = 0.0,
                M_MeanAnomalyAtEpoch = 0.0,
                // q deliberately omitted so the fallback at Kepler.cs:408-410 runs.
            };

            Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd, SolverTolerance);

            // CORRECT: q = a(1 - e) = 2.0 * 0.5 = 1.0
            elements.q_Perihelion_au.Should().NotBeNull();
            elements.q_Perihelion_au!.Value.Should().BeApproximately(1.0, 1e-12,
                "perihelion distance is a*(1-e), not a*(1+e)");
        }

        [Test]
        public void EllipticPropagation_TimeShiftAdvancesMeanAnomalyAtMeanMotion() {
            // n^2 = mu/a^3. For a = 1 AU and Sun mu (~2.959e-4 au^3/d^2), n ~= 0.01720 rad/day,
            // which is Earth's daily mean motion. Over 100 days the mean anomaly should
            // advance by ~1.720 rad. REF: classical two-body mean motion formula.
            var elements = MakeAtEpochAt(meanAnomalyRad: 0.0, eccentricity: 0.1, semiMajorAxisAu: 1.0);

            var position = Kepler.CalculateOrbitalElements(elements, elements.Epoch_jd + 100.0, SolverTolerance);

            // Sun MU in au^3/d^2: GravitationalParameter.Sun via the conversion factor.
            var n = Math.Sqrt(Kepler.GravitationalParameter.Sun.Parameter_au3_d2 / 1.0);
            position.M_MeanAnomaly_rad.Should().BeApproximately(n * 100.0, 1e-12);
        }
    }
}
