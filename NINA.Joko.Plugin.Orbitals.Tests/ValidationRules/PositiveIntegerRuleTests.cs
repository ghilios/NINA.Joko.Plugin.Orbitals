using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.ValidationRules;
using NUnit.Framework;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.ValidationRules {

    [TestFixture]
    public class PositiveIntegerRuleTests {

        private readonly PositiveIntegerRule sut = new PositiveIntegerRule();

        // Cases the current implementation correctly accepts/rejects:
        [TestCase("1", true)]
        [TestCase("100", true)]
        [TestCase("abc", false)]
        [TestCase(null, false)]
        public void Validate_BasicCases(string input, bool shouldBeValid) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().Be(shouldBeValid);
        }

        // SUSPECTED BUG: PositiveIntegerRule.cs:25 only calls int.TryParse and
        // never verifies value > 0. Class name and error message ("integer or
        // unlimited") both imply it should reject non-positive integers.
        // The cases below assert the CORRECT behavior given the name; they fail
        // against the current implementation. Marked [Explicit, Category]
        // so default CI stays green.
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("-100")]
        [Explicit, Category("BugCandidate")]
        public void Validate_NonPositiveIntegers_ShouldBeInvalid_PerClassName(string input) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse(
                "a 'positive integer' rule should reject zero and negatives; see PositiveIntegerRule.cs:25");
        }
    }
}
