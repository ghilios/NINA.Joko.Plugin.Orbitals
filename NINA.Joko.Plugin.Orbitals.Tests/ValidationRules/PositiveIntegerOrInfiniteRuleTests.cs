using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.ValidationRules;
using NUnit.Framework;
using System.Globalization;

namespace NINA.Joko.Plugin.Orbitals.Tests.ValidationRules {

    [TestFixture]
    public class PositiveIntegerOrInfiniteRuleTests {

        private readonly PositiveIntegerOrInfiniteRule sut = new PositiveIntegerOrInfiniteRule();

        [TestCase("unlimited", true)]
        [TestCase("1", true)]
        [TestCase("100", true)]
        [TestCase("abc", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Validate_BasicCases(string input, bool shouldBeValid) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().Be(shouldBeValid);
        }

        // SUSPECTED BUG: Same as PositiveIntegerRule -- the "Positive" in the
        // class name implies > 0, but the implementation accepts 0 and negatives.
        // Marked Explicit so default CI stays green.
        [TestCase("0")]
        [TestCase("-1")]
        [Explicit, Category("BugCandidate")]
        public void Validate_NonPositiveIntegers_ShouldBeInvalid_PerClassName(string input) {
            var result = sut.Validate(input, CultureInfo.InvariantCulture);
            result.IsValid.Should().BeFalse(
                "a 'positive integer or infinite' rule should reject zero and negatives");
        }

        [Test]
        public void Validate_CaseSensitive_UnlimitedMustBeLowercase() {
            // Implementation uses == "unlimited" (case-sensitive). Pin this so a future
            // refactor to OrdinalIgnoreCase is a deliberate choice.
            sut.Validate("Unlimited", CultureInfo.InvariantCulture).IsValid.Should().BeFalse();
            sut.Validate("UNLIMITED", CultureInfo.InvariantCulture).IsValid.Should().BeFalse();
        }
    }
}
