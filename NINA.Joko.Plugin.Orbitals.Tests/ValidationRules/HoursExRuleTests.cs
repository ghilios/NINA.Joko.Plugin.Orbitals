using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.ValidationRules;
using NUnit.Framework;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.ValidationRules {

    [TestFixture]
    public class HoursExRuleTests {

        private readonly HoursExRule sut = new HoursExRule();

        // "HoursEx" reads as "hours, exclusive of +/-24" -- a valid hour offset
        // lies strictly between -24 and +24 (since +/-24 wrap back to 0).
        [TestCase("0", true)]
        [TestCase("1", true)]
        [TestCase("-1", true)]
        [TestCase("23", true)]
        [TestCase("-23", true)]
        [TestCase("24", false)]
        [TestCase("-24", false)]
        [TestCase("25", false)]
        [TestCase("-25", false)]
        [TestCase("100", false)]
        [TestCase("abc", false)]
        [TestCase("", false)]
        [TestCase("1.5", false)] // not an integer
        public void Validate_NumericInput_ReturnsExpectedResult(string input, bool shouldBeValid) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().Be(shouldBeValid);
        }
    }
}
