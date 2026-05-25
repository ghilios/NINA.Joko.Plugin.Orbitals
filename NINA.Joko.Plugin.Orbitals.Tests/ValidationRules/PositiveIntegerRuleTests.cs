using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.ValidationRules;
using NUnit.Framework;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.ValidationRules {

    [TestFixture]
    public class PositiveIntegerRuleTests {

        private readonly PositiveIntegerRule sut = new PositiveIntegerRule();

        [TestCase("1", true)]
        [TestCase("100", true)]
        [TestCase("0", true)]
        [TestCase("-1", false)]
        [TestCase("-100", false)]
        [TestCase("abc", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Validate(string input, bool shouldBeValid) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().Be(shouldBeValid);
        }
    }
}
