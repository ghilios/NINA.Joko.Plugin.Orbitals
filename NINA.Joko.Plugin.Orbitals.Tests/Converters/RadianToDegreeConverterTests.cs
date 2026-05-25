using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Converters;
using NUnit.Framework;
using System;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.Converters {

    [TestFixture]
    public class RadianToDegreeConverterTests {

        private readonly RadianToDegreeConverter sut = new RadianToDegreeConverter();

        [TestCase(0.0, 0.0)]
        [TestCase(Math.PI, 180.0)]
        [TestCase(Math.PI / 2, 90.0)]
        [TestCase(2 * Math.PI, 360.0)]
        [TestCase(-Math.PI, -180.0)]
        [TestCase(1.0, 57.29577951308232)] // 1 rad in degrees
        public void Convert_NumericInput_ReturnsDegrees(double radians, double expectedDegrees) {
            var result = sut.Convert(radians, typeof(double), null, CultureInfo.InvariantCulture);

            result.Should().BeOfType<double>();
            ((double)result).Should().BeApproximately(expectedDegrees, 1e-10);
        }

        [Test]
        public void Convert_NullInput_ReturnsNull() {
            sut.Convert(null, typeof(double), null, CultureInfo.InvariantCulture).Should().BeNull();
        }

        [Test]
        public void Convert_NonNumericString_ReturnsNull() {
            sut.Convert("not a number", typeof(double), null, CultureInfo.InvariantCulture).Should().BeNull();
        }

        [Test]
        public void Convert_NumericString_Parsed() {
            // Defensive: implementation does double.TryParse(value.ToString()) without a
            // culture, so it uses the current culture. Test passes a culture-neutral string.
            var result = sut.Convert("1", typeof(double), null, CultureInfo.InvariantCulture);
            ((double)result).Should().BeApproximately(57.29577951308232, 1e-10);
        }

        [Test]
        public void ConvertBack_Throws() {
            Action act = () => sut.ConvertBack(180.0, typeof(double), null, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }
}
