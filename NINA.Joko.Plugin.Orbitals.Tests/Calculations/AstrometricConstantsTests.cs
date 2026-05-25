using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture]
    public class AstrometricConstantsTests {

        // NormalizeRadians is documented as mapping to the half-open interval (-pi, +pi].
        // Both +pi and -pi map to +pi (the high end is closed, the low end open).
        // REF: Standard astrodynamics convention used by NOVAS/SOFA wrappers.
        [TestCase(0.0, 0.0)]
        [TestCase(0.5, 0.5)]
        [TestCase(Math.PI, Math.PI)]
        [TestCase(-Math.PI, Math.PI)]
        [TestCase(2 * Math.PI, 0.0)]
        [TestCase(-2 * Math.PI, 0.0)]
        [TestCase(3 * Math.PI, Math.PI)]
        [TestCase(-3 * Math.PI, Math.PI)]
        [TestCase(Math.PI / 2, Math.PI / 2)]
        [TestCase(-Math.PI / 2, -Math.PI / 2)]
        public void NormalizeRadians_MapsToHalfOpenIntervalCenteredAtZero(double input, double expected) {
            var actual = AstrometricConstants.NormalizeRadians(input);
            actual.Should().BeApproximately(expected, 1e-12);
        }

        [Test]
        public void NormalizeRadians_JustAbovePi_WrapsToJustAboveMinusPi() {
            // pi + 1e-6  ->  pi + 1e-6 - 2pi  =  -pi + 1e-6
            var actual = AstrometricConstants.NormalizeRadians(Math.PI + 1e-6);
            actual.Should().BeApproximately(-Math.PI + 1e-6, 1e-12);
        }

        [Test]
        public void NormalizeRadians_JustBelowMinusPi_WrapsToJustBelowPi() {
            // -pi - 1e-6  ->  +pi - 1e-6 (lands at the closed end)
            var actual = AstrometricConstants.NormalizeRadians(-Math.PI - 1e-6);
            actual.Should().BeApproximately(Math.PI - 1e-6, 1e-12);
        }

        [Test]
        public void J2000MeanObliquity_MatchesIAUReference() {
            // REF: IAU 2006 (Capitaine et al.) ecliptic obliquity at J2000.0
            // = 84381.406 arcsec = 23.4392794444... degrees.
            // IAU 1976 gives 23.43929111... deg (84381.448 arcsec).
            // Tolerance of 5e-4 deg (~1.8 arcsec) accepts either convention,
            // since which one SOFA uses depends on the underlying library version.
            var expected = 23.4393; // midpoint, accepts both IAU 2006 and 1976
            AstrometricConstants.J2000MeanObliquity.Degree.Should().BeApproximately(expected, 5e-4);
        }

        [Test]
        public void KmPerAu_ExactlyMatchesIAU2012Nominal() {
            // REF: IAU 2012 Resolution B2: 1 au = 149,597,870,700 m exactly.
            AstrometricConstants.KM_PER_AU.Should().Be(149597870.700);
        }

        [Test]
        public void MPerAu_IsExactlyOneThousandTimesKmPerAu() {
            AstrometricConstants.M_PER_AU.Should().Be(AstrometricConstants.KM_PER_AU * 1000d);
        }

        [Test]
        public void SecPerDay_MatchesSIDefinition() {
            AstrometricConstants.SEC_PER_DAY.Should().Be(86400);
        }

        [Test]
        public void TwoPi_IsTwicePi() {
            AstrometricConstants.TWO_PI.Should().Be(2d * Math.PI);
        }

        [Test]
        public void RadPerDeg_IsPiOverOneEighty() {
            AstrometricConstants.RAD_PER_DEG.Should().Be(Math.PI / 180d);
        }

        [Test]
        public void SiderealRate_MatchesPublishedConstant() {
            // REF: IAU sidereal rate ~= 15.04106717866910 arcsec/sec of UT1.
            // Implementation rounds to 15.0410686 (8 sig figs).
            AstrometricConstants.SIDEREAL_RATE_ARCSEC_PER_SI_SEC
                .Should().BeApproximately(15.04106717866910, 1e-5);
        }
    }
}
