using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Position-independence demonstration tests (plan §9.2).
    ///
    /// For a set of sky anchors and (sep, PA) intents, asserts:
    ///   1. AngularSeparation(anchor, ApplyOffset(anchor, sep, PA)) recovers the
    ///      requested separation within 1e-4 arcsec at every anchor.
    ///   2. The raw RA offset varies measurably between anchors at different Dec,
    ///      proving that (sep, PA) encodes geometry-independent intent while raw
    ///      ΔRA is position-dependent.
    ///   3. The first-order flat-sky east/north components approximate sep·sin(PA)
    ///      and sep·cos(PA) at every anchor (tangent-plane consistency).
    /// </summary>
    [TestFixture]
    public class OrbitalOffsetEquivalenceTests {

        // ── Anchors ───────────────────────────────────────────────────────────────

        // Four well-separated sky anchors, all at different Dec values.
        private static readonly (string label, double raH, double decDeg)[] Anchors = {
            ("A", 0.0,   0.0),
            ("B", 6.0,  30.0),
            ("C", 18.0, 60.0),
            ("D", 12.0, -20.0),
        };

        // ── Intents ───────────────────────────────────────────────────────────────

        // Three (sep arcsec, PA deg) pairs that exercise different directions and scales.
        private static readonly (double sepArcsec, double paDeg)[] Intents = {
            (600.0,    45.0),   // 10 arcmin, NE
            (1800.0,  270.0),   // 30 arcmin, West
            (5400.0,  135.0),   //  1.5 deg,  SE
        };

        // ── Test 1: separation is recovered at every anchor ───────────────────────

        [Test]
        public void ApplyOffset_RecoversSeparation_AtEveryAnchor() {
            foreach (var (label, raH, decDeg) in Anchors) {
                var anchor = new Coordinates(Angle.ByHours(raH), Angle.ByDegree(decDeg), Epoch.J2000);

                foreach (var (sepArcsec, paDeg) in Intents) {
                    var result = OrbitalOffsetMath.ApplyOffset(anchor, sepArcsec, paDeg);
                    double recovered = OrbitalOffsetMath.AngularSeparation(anchor, result);

                    recovered.Should().BeApproximately(sepArcsec, 1e-4,
                        $"anchor {label} ({raH}h, {decDeg}°), intent ({sepArcsec}\", {paDeg}°): " +
                        "AngularSeparation(anchor, ApplyOffset) must recover the requested separation");
                }
            }
        }

        // ── Test 2: raw RA offset differs between anchors A and C ────────────────

        /// <summary>
        /// Proves that applying the same (sep, PA) intent produces different raw ΔRA
        /// at anchor A (Dec=0°) vs. anchor C (Dec=+60°).  The cos(Dec) foreshortening
        /// is roughly 2× between these two declinations, so for a 30-arcmin intent the
        /// ΔRA difference should comfortably exceed 0.01 h.
        /// </summary>
        [Test]
        public void ApplyOffset_RawRAOffset_DiffersBetweenAnchors_ProvingPositionDependence() {
            // Anchor A at RA=6h (not 0h) to avoid RA wrap-around when a westward offset
            // would place the result near 24h / 0h and make deltaRA_A spuriously large.
            var anchorA = new Coordinates(Angle.ByHours(6.0),  Angle.ByDegree(0.0),  Epoch.J2000);
            var anchorC = new Coordinates(Angle.ByHours(18.0), Angle.ByDegree(60.0), Epoch.J2000);

            // Use the 30-arcmin West intent (PA=270°): pure RA offset, maximises cos(Dec) difference.
            double sep  = 1800.0;  // 30 arcmin in arcsec
            double pa   = 270.0;   // West

            var resultA = OrbitalOffsetMath.ApplyOffset(anchorA, sep, pa);
            var resultC = OrbitalOffsetMath.ApplyOffset(anchorC, sep, pa);

            double deltaRA_A = resultA.RA - anchorA.RA;
            double deltaRA_C = resultC.RA - anchorC.RA;

            // Both should represent the same angular intent …
            double sepA = OrbitalOffsetMath.AngularSeparation(anchorA, resultA);
            double sepC = OrbitalOffsetMath.AngularSeparation(anchorC, resultC);
            sepA.Should().BeApproximately(sep, 1e-4, "anchor A: separation must be correct");
            sepC.Should().BeApproximately(sep, 1e-4, "anchor C: separation must be correct");

            // … but the raw ΔRA in hours must differ measurably due to cos(Dec) foreshortening.
            // At Dec=0°:  ΔRA ≈ −(1800/3600)/15 h ≈ −0.0333 h  (no foreshortening)
            // At Dec=60°: ΔRA ≈ −0.0333 / cos(60°) = −0.0667 h  (foreshortened by ×2)
            // Difference ≈ 0.033 h >> 0.01 h threshold.
            double raDiff = Math.Abs(deltaRA_A - deltaRA_C);
            raDiff.Should().BeGreaterThan(0.01,
                "the same angular intent expressed at Dec=0° vs Dec=+60° must yield " +
                "different raw ΔRA, demonstrating position-dependence of naïve RA offsets");
        }

        // ── Test 3: tangent-plane components are consistent across anchors ──────

        /// <summary>
        /// Plan §9.2 item 3 – gnomonic / tangent-plane projection equivalence.
        ///
        /// For a canonical (sep=600 arcsec, PA=45°) intent the first-order east-north
        /// tangent-plane components should approximate sep·sin(PA) and sep·cos(PA).
        ///
        /// Because <see cref="OrbitalOffsetMath.ApplyOffset"/> encodes a geometry-
        /// independent intent, the flat-sky approximation
        ///   Δeast  = (result.RA  − anchor.RA) · 15 · cos(anchor.Dec) · 3600   arcsec
        ///   Δnorth = (result.Dec − anchor.Dec) · 3600                          arcsec
        /// should agree with sep·sin(PA) / sep·cos(PA) up to the second-order
        /// spherical-curvature correction O(sep²/R²), which for sep=600 arcsec and
        /// the highest-declination anchor (C, Dec=+60°) is at most ~2 arcsec.
        ///
        /// Tolerance note: the flat-formula error scales as ~(sep/206265)² · sep, so
        /// for sep=600 arcsec the maximum error across all anchors is ≈1.5 arcsec.
        /// We use 2.0 arcsec as a comfortable bound.
        /// </summary>
        [Test]
        public void ApplyOffset_TangentPlaneComponents_AreConsistentAcrossAnchors() {
            const double sep   = 600.0;   // 10 arcmin
            const double pa    = 45.0;    // NE
            const double tol   = 2.0;     // arcsec; covers second-order spherical curvature

            double paRad = pa * Math.PI / 180.0;
            double expectedEast  = sep * Math.Sin(paRad);  // ≈ 424.264 arcsec
            double expectedNorth = sep * Math.Cos(paRad);  // ≈ 424.264 arcsec

            foreach (var (label, raH, decDeg) in Anchors) {
                var anchor = new Coordinates(Angle.ByHours(raH), Angle.ByDegree(decDeg), Epoch.J2000);
                var result = OrbitalOffsetMath.ApplyOffset(anchor, sep, pa);

                // First-order flat-sky east component (arcsec):
                //   Δeast = ΔRA_hours · 15 · cos(anchor.Dec) · 3600
                double deltaEast = (result.RA - anchor.RA) * 15.0
                                   * Math.Cos(anchor.Dec * Math.PI / 180.0) * 3600.0;

                // North component (arcsec):
                //   Δnorth = ΔDec_deg · 3600
                double deltaNorth = (result.Dec - anchor.Dec) * 3600.0;

                deltaEast.Should().BeApproximately(expectedEast, tol,
                    $"anchor {label} ({raH}h, {decDeg}°): flat-sky east component " +
                    $"must approximate sep·sin(PA) = {expectedEast:F3} arcsec within {tol} arcsec");
                deltaNorth.Should().BeApproximately(expectedNorth, tol,
                    $"anchor {label} ({raH}h, {decDeg}°): flat-sky north component " +
                    $"must approximate sep·cos(PA) = {expectedNorth:F3} arcsec within {tol} arcsec");
            }
        }
    }
}
