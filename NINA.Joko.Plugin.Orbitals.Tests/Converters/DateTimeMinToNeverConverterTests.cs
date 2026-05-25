using FluentAssertions;
using NINA.Joko.Plugin.Orbitals;
using NINA.Joko.Plugin.Orbitals.Converters;
using NUnit.Framework;
using System;
using System.Globalization;
using System.Reflection;

namespace NINA.Joko.Plugin.Orbitals.Tests.Converters {

    [TestFixture]
    public class DateTimeMinToNeverConverterTests {

        private readonly DateTimeMinToNeverConverter sut = new DateTimeMinToNeverConverter();

        [OneTimeSetUp]
        public void SeedSystemCultureInfo() {
            // OrbitalsPlugin.SystemCultureInfo is normally set by the plugin's
            // ImportingConstructor. Outside of NINA we have to seed it via reflection
            // (private setter) so the converter's formatting path is exercised against
            // a known culture rather than dotnet's default.
            var prop = typeof(OrbitalsPlugin).GetProperty(
                nameof(OrbitalsPlugin.SystemCultureInfo),
                BindingFlags.Public | BindingFlags.Static);
            prop.Should().NotBeNull("the converter reads OrbitalsPlugin.SystemCultureInfo");
            prop!.SetValue(null, CultureInfo.InvariantCulture);
        }

        [Test]
        public void Convert_Null_ReturnsNever() {
            sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("Never");
        }

        [Test]
        public void Convert_DateTimeMinValue_ReturnsNever() {
            sut.Convert(DateTime.MinValue, typeof(string), null, CultureInfo.InvariantCulture).Should().Be("Never");
        }

        [Test]
        public void Convert_DateTimeBelowMinValueSemantics_ReturnsNever() {
            // Implementation checks `<= DateTime.MinValue`, so MinValue itself qualifies.
            // Sanity: a value exactly at MinValue is classified as "Never".
            sut.Convert(DateTime.MinValue.AddTicks(0), typeof(string), null, CultureInfo.InvariantCulture)
                .Should().Be("Never");
        }

        [Test]
        public void Convert_RealDate_FormatsViaSystemCultureInfo() {
            var when = new DateTime(2024, 3, 15, 10, 30, 0, DateTimeKind.Utc);
            var expected = when.ToString(CultureInfo.InvariantCulture);

            var actual = sut.Convert(when, typeof(string), null, CultureInfo.InvariantCulture) as string;

            actual.Should().Be(expected);
        }

        [Test]
        public void ConvertBack_Throws() {
            Action act = () => sut.ConvertBack("any", typeof(DateTime), null, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }
}
