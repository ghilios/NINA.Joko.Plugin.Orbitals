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
            var anchorA = new Coordinates(Angle.ByHours(0.0),  Angle.ByDegree(0.0),  Epoch.J2000);
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

            // … but the raw ΔRA in hours must differ measurably.
            // At Dec=0°: ΔRA ≈ −(1800/3600)/15 h ≈ −0.0333 h
            // At Dec=60°: ΔRA ≈ −0.0333 / cos(60°) = −0.0667 h
            // Difference ≈ 0.033 h >> 0.01 h threshold.
            double raDiff = Math.Abs(deltaRA_A - deltaRA_C);
            raDiff.Should().BeGreaterThan(0.01,
                "the same angular intent expressed at Dec=0° vs Dec=+60° must yield " +
                "different raw ΔRA, demonstrating position-dependence of naïve RA offsets");
        }
    }
}
