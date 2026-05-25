using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Converters;
using NUnit.Framework;
using System;
using System.ComponentModel;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.Converters {

    [TestFixture]
    public class EnumStaticDescriptionValueConverterTests {

        private readonly EnumStaticDescriptionValueConverter sut = new EnumStaticDescriptionValueConverter();

        public enum SampleEnum {
            [System.ComponentModel.Description("Friendly Name")] WithDescription,
            WithoutDescription,
            [System.ComponentModel.Description("")] WithEmptyDescription,
        }

        [Test]
        public void Convert_WithDescriptionAttribute_ReturnsDescription() {
            var result = sut.Convert(SampleEnum.WithDescription, typeof(string), null, CultureInfo.InvariantCulture);
            result.Should().Be("Friendly Name");
        }

        [Test]
        public void Convert_WithoutDescriptionAttribute_ReturnsEnumName() {
            var result = sut.Convert(SampleEnum.WithoutDescription, typeof(string), null, CultureInfo.InvariantCulture);
            result.Should().Be("WithoutDescription");
        }

        [Test]
        public void Convert_EmptyDescription_FallsBackToEnumName() {
            var result = sut.Convert(SampleEnum.WithEmptyDescription, typeof(string), null, CultureInfo.InvariantCulture);
            result.Should().Be("WithEmptyDescription");
        }

        [Test]
        public void Convert_NonStringTargetType_Throws() {
            Action act = () => sut.Convert(SampleEnum.WithDescription, typeof(int), null, CultureInfo.InvariantCulture);
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void Convert_NullValue_ReturnsEmptyString() {
            // value is null -> value?.GetType() is null -> FieldInfo is null -> returns empty.
            var result = sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
            result.Should().Be(string.Empty);
        }

        [Test]
        public void ConvertBack_Throws() {
            Action act = () => sut.ConvertBack("any", typeof(SampleEnum), null, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }
}
