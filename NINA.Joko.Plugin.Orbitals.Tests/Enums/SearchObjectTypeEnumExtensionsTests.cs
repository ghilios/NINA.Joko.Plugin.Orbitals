using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Enums;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Enums {

    [TestFixture]
    public class SearchObjectTypeEnumExtensionsTests {

        [TestCase(SearchObjectTypeEnum.Comet, OrbitalObjectTypeEnum.Comet)]
        [TestCase(SearchObjectTypeEnum.NumberedAsteroids, OrbitalObjectTypeEnum.NumberedAsteroids)]
        [TestCase(SearchObjectTypeEnum.UnnumberedAsteroids, OrbitalObjectTypeEnum.UnnumberedAsteroids)]
        public void ToOrbitalObjectTypeEnum_ConvertibleValues_MapAcrossWithSameIntegerValue(
            SearchObjectTypeEnum input, OrbitalObjectTypeEnum expected) {
            input.ToOrbitalObjectTypeEnum().Should().Be(expected);
        }

        [TestCase(SearchObjectTypeEnum.SolarSystemBody)]
        [TestCase(SearchObjectTypeEnum.JWST)]
        [TestCase(SearchObjectTypeEnum.ManualTLE)]
        public void ToOrbitalObjectTypeEnum_NonConvertibleValues_Throw(SearchObjectTypeEnum input) {
            Action act = () => input.ToOrbitalObjectTypeEnum();
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void SearchEnum_AndOrbitalEnum_OverlappingMembersAgreeOnIntegerValue() {
            // The implementation casts (OrbitalObjectTypeEnum)objectType for the
            // convertible members, so the two enums must agree on the int values
            // for Comet, NumberedAsteroids, UnnumberedAsteroids. Pin that contract.
            ((int)SearchObjectTypeEnum.Comet).Should().Be((int)OrbitalObjectTypeEnum.Comet);
            ((int)SearchObjectTypeEnum.NumberedAsteroids).Should().Be((int)OrbitalObjectTypeEnum.NumberedAsteroids);
            ((int)SearchObjectTypeEnum.UnnumberedAsteroids).Should().Be((int)OrbitalObjectTypeEnum.UnnumberedAsteroids);
        }
    }
}
