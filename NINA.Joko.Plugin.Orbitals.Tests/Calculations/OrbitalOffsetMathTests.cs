using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Unit tests for <see cref="OrbitalOffsetMath"/>.
    ///
    /// Coverage:
    ///   §9.1 – AngularSeparation symmetry, known separations, PA quadrants, round-trip, pole.
    /// </summary>
    [TestFixture]
    public class OrbitalOffsetMathTests {

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Coordinates C(double raHours, double decDeg) =>
            new Coordinates(Angle.ByHours(raHours), Angle.ByDegree(decDeg), Epoch.J2000);

        // ── AngularSeparation – Symmetry ─────────────────────────────────────────

        // Plan §9.1 – Separation symmetry over a grid of (α, δ) pairs.
        [TestCase(0.0, 0.0, 1.0, 30.0)]
        [TestCase(6.0, 45.0, 18.0, -45.0)]
        [TestCase(12.0, -60.0, 0.0, 60.0)]
        [TestCase(23.5, 80.0, 0.5, -80.0)]
        [TestCase(3.0, 0.0, 21.0, 0.0)]
        public void AngularSeparation_IsSymmetric(double ra1, double dec1, double ra2, double dec2) {
            var a = C(ra1, dec1);
            var b = C(ra2, dec2);

            double ab = OrbitalOffsetMath.AngularSeparation(a, b);
            double ba = OrbitalOffsetMath.AngularSeparation(b, a);

            ab.Should().BeApproximately(ba, 1e-10, "separation must be symmetric");
        }

        // ── AngularSeparation – Known values ─────────────────────────────────────

        // Plan §9.1 – (0h, 0°) to (1h, 0°) on equator = exactly 15° = 54000 arcsec.
        [Test]
        public void AngularSeparation_OneHourOnEquator_Is54000Arcsec() {
            var c1 = C(0.0, 0.0);
            var c2 = C(1.0, 0.0);

            double sep = OrbitalOffsetMath.AngularSeparation(c1, c2);

            sep.Should().BeApproximately(54000.0, 1e-6, "1 h RA on equator = 15° = 54 000\"");
        }

        // Plan §9.1 – (0h, 60°) to (1h, 60°).
        // Great-circle distance: cos(d) = sin²(60°) + cos²(60°)·cos(15°)
        // = 0.75 + 0.25·cos(15°) ≈ 0.75 + 0.25·0.96593 = 0.99148
        // d = acos(0.99148) ≈ 7.4839° ≈ 26942 arcsec
        [Test]
        public void AngularSeparation_OneHourAt60DegDec_IsApprox26942Arcsec() {
            var c1 = C(0.0, 60.0);
            var c2 = C(1.0, 60.0);

            // Analytical value
            double cosD = Math.Sin(60.0 * Math.PI / 180) * Math.Sin(60.0 * Math.PI / 180)
                        + Math.Cos(60.0 * Math.PI / 180) * Math.Cos(60.0 * Math.PI / 180)
                          * Math.Cos(15.0 * Math.PI / 180);
            double expectedArcsec = Math.Acos(cosD) * (180.0 * 3600.0 / Math.PI);

            double sep = OrbitalOffsetMath.AngularSeparation(c1, c2);

            sep.Should().BeApproximately(expectedArcsec, 0.01,
                "1 h RA at Dec +60° shrinks by cos(60°) factor");
        }

        // ── PositionAngleNToE – Quadrants ────────────────────────────────────────

        // Plan §9.1 – North: (0h,0°) → (0h,+1°) ≈ 0°
        [Test]
        public void PositionAngle_North_IsZero() {
            double pa = OrbitalOffsetMath.PositionAngleNToE(C(0.0, 0.0), C(0.0, 1.0));
            pa.Should().BeApproximately(0.0, 0.01, "north displacement → PA ≈ 0°");
        }

        // Plan §9.1 – East: (0h,0°) → (+1m of RA, 0°) ≈ 90°
        [Test]
        public void PositionAngle_East_Is90() {
            double pa = OrbitalOffsetMath.PositionAngleNToE(C(0.0, 0.0), C(1.0 / 60.0, 0.0));
            pa.Should().BeApproximately(90.0, 0.01, "eastward RA increase → PA ≈ 90°");
        }

        // Plan §9.1 – South: (0h,0°) → (0h,-1°) ≈ 180°
        [Test]
        public void PositionAngle_South_Is180() {
            double pa = OrbitalOffsetMath.PositionAngleNToE(C(0.0, 0.0), C(0.0, -1.0));
            pa.Should().BeApproximately(180.0, 0.01, "southward displacement → PA ≈ 180°");
        }

        // Plan §9.1 – West: (0h,0°) → (-1m of RA, 0°) ≈ 270°
        [Test]
        public void PositionAngle_West_Is270() {
            double pa = OrbitalOffsetMath.PositionAngleNToE(C(0.0, 0.0), C(-1.0 / 60.0, 0.0));
            pa.Should().BeApproximately(270.0, 0.01, "westward RA decrease → PA ≈ 270°");
        }

        // ── Round-trip ───────────────────────────────────────────────────────────

        // Plan §9.1 – Round-trip: ApplyOffset then back-check separation and PA.
        [TestCase(1.0)]          // 1 arcsec
        [TestCase(60.0)]         // 1 arcmin
        [TestCase(600.0)]        // 10 arcmin
        [TestCase(3600.0)]       // 1°
        [TestCase(108000.0)]     // 30°
        public void RoundTrip_SeparationAndPA_RecoverWithinTolerance(double sepArcsec) {
            var anchor = C(6.0, 20.0);   // arbitrary but well away from poles

            for (int paDeg = 0; paDeg < 360; paDeg += 30) {
                var result = OrbitalOffsetMath.ApplyOffset(anchor, sepArcsec, paDeg);

                double recoveredSep = OrbitalOffsetMath.AngularSeparation(anchor, result);
                recoveredSep.Should().BeApproximately(sepArcsec, 1e-4,
                    $"sep round-trip at sep={sepArcsec}\", PA={paDeg}°");

                // PA only meaningful when sep > 0
                if (sepArcsec > 0.0) {
                    double recoveredPa = OrbitalOffsetMath.PositionAngleNToE(anchor, result);
                    // Normalise difference into (-180, 180]
                    double diff = ((recoveredPa - paDeg) % 360.0 + 360.0) % 360.0;
                    if (diff > 180.0) diff -= 360.0;
                    Math.Abs(diff).Should().BeLessOrEqualTo(1e-3,
                        $"PA round-trip at sep={sepArcsec}\", PA={paDeg}°");
                }
            }
        }

        // ── PositionAngle – 45° diagonal ────────────────────────────────────────

        // Plan §9.1 – NE diagonal: (0h,0°) → (+30″/cos(0°) east, +30″ north) ≈ 45°.
        // The east offset in RA-hours for 30 arcsec at Dec=0° is 30/(3600*15) h.
        // The north offset in Dec-degrees is 30/3600 °.
        // The resulting PA from the north-through-east formula should be ≈ 45°.
        [Test]
        public void PositionAngle_NEDiagonal_Is45Degrees() {
            var from = C(0.0, 0.0);

            // Move 30 arcsec east and 30 arcsec north from equator.
            // At Dec=0° east offset in RA-hours = 30″ / (3600 * 15) h.
            double eastOffsetHours = 30.0 / (3600.0 * 15.0);
            double northOffsetDeg  = 30.0 / 3600.0;
            var to = C(eastOffsetHours, northOffsetDeg);

            double pa = OrbitalOffsetMath.PositionAngleNToE(from, to);

            pa.Should().BeApproximately(45.0, 0.1,
                "NE diagonal (equal east and north offsets at equator) → PA ≈ 45°");
        }

        // ── PositionAngle – Agreement with AstroUtil.CalculatePositionAngle ────

        // Plan §9.1 – Cross-check our PositionAngleNToE against NINA's built-in
        // AstroUtil.CalculatePositionAngle (Atan-based formula).
        //
        // AstroUtil.CalculatePositionAngle(a1deg, a2deg, d1deg, d2deg) computes:
        //   θ = atan( sin(a1-a2) / (cos(d2)·tan(d1) − sin(d2)·cos(a1-a2)) )
        // where a1=from_RA, a2=to_RA.  Because sin(a1-a2) is negative when the target
        // is east of the reference, NINA's formula gives East→270° while our N-through-E
        // convention gives East→90°.  The two values satisfy:
        //   ninaPA = (360 − ourPA) mod 360   for purely RA offsets.
        //
        // The formulas AGREE numerically (ninaPA ≈ ourPA) only when the target is in the
        // NE or NW quadrant, i.e. when the northward component dominates.  We test that
        // domain exclusively and use a tolerance of 0.01° because the Atan approximation
        // differs from our atan2 formula by O(ΔRA²) even in the valid quadrant.
        //
        // NINA's atan (not atan2) also has quadrant ambiguity near PA=180° (pure south),
        // so we skip any southward cases.
        [TestCase(0.0, 0.0, 0.0, 1.0)]           // Pure North → PA = 0° (both agree exactly)
        [TestCase(0.0, 0.0, -1.0/60.0, 1.0)]     // NW (west-north) → PA ≈ 346°
        [TestCase(0.0, 0.0,  1.0/60.0, 1.0)]     // NE (east-north) → PA ≈ 14°
        public void PositionAngle_AgreesWithAstroUtil_NorthernHalf(
                double fromRaH, double fromDecDeg, double toRaH, double toDecDeg) {
            var from = C(fromRaH, fromDecDeg);
            var to   = C(toRaH,   toDecDeg);

            // Our N-through-E formula.
            double ourPA = OrbitalOffsetMath.PositionAngleNToE(from, to);

            // NINA's Atan-based formula.  Arguments: RA in degrees (= hours * 15).
            double ninaRaw = AstroUtil.CalculatePositionAngle(
                fromRaH * 15.0, toRaH * 15.0, fromDecDeg, toDecDeg);
            // Wrap NINA result into [0, 360).
            double ninaPA = ((ninaRaw % 360.0) + 360.0) % 360.0;

            // In the NE/NW quadrant with north dominating, both formulas give the same
            // numerical result (the sin/atan ratio is the same for small RA displacements
            // with a large northward component).  Tolerance 0.01° accommodates the
            // atan vs atan2 second-order difference.
            ourPA.Should().BeApproximately(ninaPA, 0.01,
                $"PositionAngleNToE must agree with AstroUtil.CalculatePositionAngle " +
                $"in the northern half for ({fromRaH}h, {fromDecDeg}°) → ({toRaH}h, {toDecDeg}°)");
        }

        // ── Pole test ────────────────────────────────────────────────────────────

        // Plan §9.1 – ApplyOffset near Dec=+89.99° must return finite coordinates
        // and the back-calculated separation must recover within tolerance.
        [TestCase(60.0, 45.0)]
        [TestCase(3600.0, 270.0)]
        public void ApplyOffset_NearNorthPole_IsFiniteAndRoundTrips(double sepArcsec, double paDeg) {
            var polar = C(0.0, 89.99);

            var result = OrbitalOffsetMath.ApplyOffset(polar, sepArcsec, paDeg);

            double.IsNaN(result.RA).Should().BeFalse("RA must be finite near pole");
            double.IsNaN(result.Dec).Should().BeFalse("Dec must be finite near pole");
            double.IsInfinity(result.RA).Should().BeFalse("RA must not be infinite near pole");
            double.IsInfinity(result.Dec).Should().BeFalse("Dec must not be infinite near pole");

            double recoveredSep = OrbitalOffsetMath.AngularSeparation(polar, result);
            recoveredSep.Should().BeApproximately(sepArcsec, 1e-4,
                "separation round-trip must hold near the pole");
        }
    }
}
