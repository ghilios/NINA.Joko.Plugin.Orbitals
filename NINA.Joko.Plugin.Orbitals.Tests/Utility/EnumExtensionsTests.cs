using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;
using System;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture]
    public class EnumExtensionsTests {

        public enum SampleEnum {
            [System.ComponentModel.Description("My Friendly Name")] Pretty,
            Plain,
        }

        [Test]
        public void ToDescriptionString_WithDescription_ReturnsAttributeText() {
            SampleEnum.Pretty.ToDescriptionString().Should().Be("My Friendly Name");
        }

        [Test]
        public void ToDescriptionString_WithoutDescription_FallsBackToEnumName() {
            SampleEnum.Plain.ToDescriptionString().Should().Be("Plain");
        }

        [Test]
        public void ApplyQuirks_None_ReturnsRateUnchanged() {
            var rate = SiderealShiftTrackingRate.Create(raDegreesPerHour: 1.0, decDegreesPerHour: 0.5);

            var result = rate.ApplyQuirks(QuirksModeEnum.None);

            result.RADegreesPerHour.Should().Be(1.0);
            result.DecDegreesPerHour.Should().Be(0.5);
        }

        [Test]
        public void ApplyQuirks_EQMOD_MultipliesRAByTheSiderealRateConstant() {
            // EQMOD interprets RA tracking rate as arcsec/sec instead of RA-sec/sidereal-sec.
            // To compensate, the implementation multiplies RA by SIDEREAL_RATE_ARCSEC_PER_SI_SEC.
            // Dec is left unchanged.
            // Verifying against the published constant rather than re-reading it from the impl.
            const double expectedScale = 15.0410686; // AstrometricConstants.SIDEREAL_RATE_ARCSEC_PER_SI_SEC
            var rate = SiderealShiftTrackingRate.Create(raDegreesPerHour: 2.0, decDegreesPerHour: 0.5);

            var result = rate.ApplyQuirks(QuirksModeEnum.EQMOD);

            result.RADegreesPerHour.Should().BeApproximately(2.0 * expectedScale, 1e-9);
            result.DecDegreesPerHour.Should().Be(0.5);
        }

        [Test]
        public void ApplyQuirks_UnknownMode_Throws() {
            // The enum currently only has None and EQMOD; cast an undefined value to
            // confirm the implementation rejects it explicitly rather than silently
            // returning the input unchanged.
            var rate = SiderealShiftTrackingRate.Create(1.0, 1.0);
            Action act = () => rate.ApplyQuirks((QuirksModeEnum)999);
            act.Should().Throw<ArgumentException>();
        }
    }
}
