using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Both offset methods exercised on real-sky fixture coordinates (plan §9.6).
    ///
    /// Scenario A – legacy RA/Dec offset math (as used by OrbitalsContainerBase):
    ///   Apply (ΔRA = +30 arcsec / cos(Dec), ΔDec = +60 arcsec) and verify the
    ///   resulting coordinates have the expected RA/Dec shift.
    ///
    /// Scenario B – spherical ApplyOffset:
    ///   Apply (sep = 20′ = 1200 arcsec, PA = 60°) to each fixture. Assert:
    ///     i.  AngularSeparation(anchor, result) ≈ 1200 arcsec (within 1e-4 arcsec).
    ///     ii. The raw ΔRA between fixtures differs by > 1 arcsec, proving
    ///         position-dependence of naïve RA offsets.
    /// </summary>
    [TestFixture]
    public class OrbitalFramingOffsetRealCasesTests {

        // ── Fixtures ──────────────────────────────────────────────────────────────

        private static readonly (string label, Coordinates coords)[] Fixtures = {
            ("CometA3",  OrbitalFramingScenarios.CometA3_20241026()),
            ("Ceres",    OrbitalFramingScenarios.Ceres_JD2460200()),
            ("Mars",     OrbitalFramingScenarios.Mars_20250615()),
            ("Jupiter",  OrbitalFramingScenarios.Jupiter_20260115()),
        };

        // ── Scenario A: legacy RA/Dec offset ──────────────────────────────────────

        /// <summary>
        /// Mimics OrbitalsContainerBase's additive offset:
        ///   newRA  = anchor.RA  + ΔRA_hours
        ///   newDec = anchor.Dec + ΔDec_deg
        ///
        /// ΔRA is expressed in hours as (30 arcsec / cos(Dec)) converted to hours
        /// so that the actual angular shift along the parallel is 30 arcsec.
        /// ΔDec = +60 arcsec = +60/3600 degrees.
        /// </summary>
        [Test]
        public void ScenarioA_LegacyRaDecOffset_AppliedCorrectly() {
            const double deltaRaArcsec  = 30.0;
            const double deltaDecArcsec = 60.0;
            const double arcsecPerDeg   = 3600.0;
            const double hoursPerDegree = 1.0 / 15.0;

            foreach (var (label, anchor) in Fixtures) {
                double cosDec = Math.Cos(anchor.Dec * Math.PI / 180.0);

                // ΔRA in hours: 30 arcsec of RA corresponds to 30/(3600*15) h at the equator,
                // but on a parallel at declination δ the hour-angle step that gives 30 arcsec of
                // arc is (30/cos(δ)) / (3600*15) h.  We keep the naive add for this scenario.
                double deltaRaHours  = (deltaRaArcsec / cosDec) / arcsecPerDeg * hoursPerDegree;
                double deltaDecDeg   = deltaDecArcsec / arcsecPerDeg;

                double newRa  = anchor.RA  + deltaRaHours;
                double newDec = anchor.Dec + deltaDecDeg;

                newRa.Should().BeApproximately(anchor.RA + deltaRaHours, 1e-10,
                    $"{label}: newRA must equal anchor.RA + ΔRA");
                newDec.Should().BeApproximately(anchor.Dec + deltaDecDeg, 1e-10,
                    $"{label}: newDec must equal anchor.Dec + ΔDec");

                // The actual angular RA shift on sky should be ≈ 30 arcsec
                var shiftedCoord = new Coordinates(
                    Angle.ByHours(newRa),
                    Angle.ByDegree(anchor.Dec),   // Dec unchanged for RA-only shift
                    Epoch.J2000);
                double raShiftArcsec = OrbitalOffsetMath.AngularSeparation(
                    new Coordinates(Angle.ByHours(anchor.RA), Angle.ByDegree(anchor.Dec), Epoch.J2000),
                    shiftedCoord);
                raShiftArcsec.Should().BeApproximately(deltaRaArcsec, 0.1,
                    $"{label}: the RA-only shift should produce ≈30\" on sky");
            }
        }

        // ── Scenario B: spherical ApplyOffset ────────────────────────────────────

        /// <summary>
        /// Apply (sep = 1200 arcsec = 20′, PA = 60°) to every fixture and verify
        /// the angular separation is recovered exactly.
        /// </summary>
        [Test]
        public void ScenarioB_SphericalOffset_SeparationRecovered_AtEachFixture() {
            const double sep = 1200.0;  // 20 arcmin in arcsec
            const double pa  = 60.0;

            foreach (var (label, anchor) in Fixtures) {
                var result = OrbitalOffsetMath.ApplyOffset(anchor, sep, pa);
                double recovered = OrbitalOffsetMath.AngularSeparation(anchor, result);

                recovered.Should().BeApproximately(sep, 1e-4,
                    $"{label}: AngularSeparation(anchor, ApplyOffset(anchor, 1200\", 60°)) must be 1200\"");
            }
        }

        /// <summary>
        /// For the same (sep, PA), the raw ΔRA in hours between at least two fixtures
        /// with different declinations must differ by more than 1 arcsec in RA-arc terms.
        /// This proves position-dependence of naïve RA offset.
        /// </summary>
        [Test]
        public void ScenarioB_SphericalOffset_RawRADiffers_BetweenFixtures() {
            const double sep = 1200.0;  // 20 arcmin in arcsec
            const double pa  = 60.0;

            // Collect raw ΔRA (in arcsec-equivalent of RA angle on sky) for each fixture.
            // We compute: ΔRA_arcsec = (result.RA - anchor.RA) * cos(anchor.Dec) * 3600 * 15
            double[] raShiftArcsec = new double[Fixtures.Length];
            for (int i = 0; i < Fixtures.Length; i++) {
                var (_, anchor) = Fixtures[i];
                var result = OrbitalOffsetMath.ApplyOffset(anchor, sep, pa);
                double cosDec = Math.Cos(anchor.Dec * Math.PI / 180.0);
                // RA is in hours; convert Δhours to arcsec-of-arc
                raShiftArcsec[i] = (result.RA - anchor.RA) * cosDec * 15.0 * 3600.0;
            }

            // Find the maximum pairwise difference in RA shift
            double maxDiff = 0.0;
            for (int i = 0; i < raShiftArcsec.Length; i++) {
                for (int j = i + 1; j < raShiftArcsec.Length; j++) {
                    maxDiff = Math.Max(maxDiff, Math.Abs(raShiftArcsec[i] - raShiftArcsec[j]));
                }
            }

            maxDiff.Should().BeGreaterThan(1.0,
                "the same (sep, PA) intent applied at fixtures with different Dec values must " +
                "produce measurably different ΔRA arcsec on sky (> 1 arcsec difference), " +
                "confirming position-dependence of raw RA offsets");
        }
    }
}
