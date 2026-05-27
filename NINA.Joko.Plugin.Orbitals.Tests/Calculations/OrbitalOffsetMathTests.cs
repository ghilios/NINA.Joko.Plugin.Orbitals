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
        // d = acos(0.99148) ≈ 7.4756° ≈ 26912 arcsec
        [Test]
        public void AngularSeparation_OneHourAt60DegDec_IsApprox26913Arcsec() {
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
