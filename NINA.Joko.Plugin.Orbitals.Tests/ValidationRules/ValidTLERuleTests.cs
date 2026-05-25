using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.ValidationRules;
using NUnit.Framework;
using System;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.ValidationRules {

    [TestFixture]
    public class ValidTLERuleTests {

        private readonly ValidTLERule sut = new ValidTLERule();

        // REF: Canonical ISS (ZARYA) TLE, epoch 2008-Sep-20.
        // From the original NORAD format documentation; kept frozen as a
        // parser-only fixture (the epoch is far in the past, but the format is
        // what's being validated, not the orbit's freshness).
        private const string ISS_NAME = "ISS (ZARYA)";
        private const string ISS_L1 = "1 25544U 98067A   08264.51782528 -.00002182  00000-0 -11606-4 0  2927";
        private const string ISS_L2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.72125391563537";

        private static string Join(params string[] lines) => string.Join(Environment.NewLine, lines);

        [Test]
        public void Validate_ValidThreeLineTLE_Passes() {
            var result = sut.Validate(Join(ISS_NAME, ISS_L1, ISS_L2), CultureInfo.InvariantCulture);
            result.IsValid.Should().BeTrue($"because of: {result.ErrorContent}");
        }

        [Test]
        public void Validate_ValidTwoLineTLE_Passes() {
            var result = sut.Validate(Join(ISS_L1, ISS_L2), CultureInfo.InvariantCulture);
            result.IsValid.Should().BeTrue($"because of: {result.ErrorContent}");
        }

        [Test]
        public void Validate_OneLine_Invalid() {
            var result = sut.Validate(ISS_L1, CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Validate_FourLines_Invalid() {
            var result = sut.Validate(Join(ISS_NAME, ISS_L1, ISS_L2, "extra"), CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Validate_Garbage_Invalid() {
            var result = sut.Validate(Join("not a TLE", "really not"), CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Validate_Null_Invalid() {
            var result = sut.Validate(null, CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse();
        }

        [Test]
        public void Validate_EmptyString_Invalid() {
            var result = sut.Validate(string.Empty, CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse();
        }
    }
}
