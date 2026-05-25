using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Converters;
using NUnit.Framework;
using System;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.Converters {

    [TestFixture]
    public class JulianToDateTimeConverterTests {

        private readonly JulianToDateTimeConverter sut = new JulianToDateTimeConverter();

        // REF: USNO standard Julian Date reference points.
        // J2000.0 epoch is JD 2451545.0 = 2000-01-01T12:00:00 TT.
        // Unix epoch is JD 2440587.5 = 1970-01-01T00:00:00 UTC.
        // NOVAS.JulianToDateTime is implemented in managed code in NINA.Astrometry
        // (no native dependency), so this test runs without SOFA/NOVAS dlls.
        [TestCase(2451545.0, "2000-01-01")]
        [TestCase(2440587.5, "1970-01-01")]
        public void Convert_KnownJulianDate_ReturnsExpectedCalendarDate(double jd, string expectedDate) {
            var result = sut.Convert(jd, typeof(string), null, CultureInfo.InvariantCulture) as string;

            result.Should().NotBeNullOrEmpty();
            // The implementation calls DateTime.ToString() with no format -- uses thread culture.
            // Validate the underlying date portion via parsing rather than literal string match.
            DateTime.Parse(result!, CultureInfo.InvariantCulture).Date
                .Should().Be(DateTime.Parse(expectedDate, CultureInfo.InvariantCulture));
        }

        [Test]
        public void Convert_NullInput_ReturnsEmptyString() {
            sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture).Should().Be(string.Empty);
        }

        [Test]
        public void Convert_NonNumericString_ReturnsEmptyString() {
            sut.Convert("not a number", typeof(string), null, CultureInfo.InvariantCulture).Should().Be(string.Empty);
        }

        [Test]
        public void ConvertBack_Throws() {
            Action act = () => sut.ConvertBack("any", typeof(string), null, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }
}
