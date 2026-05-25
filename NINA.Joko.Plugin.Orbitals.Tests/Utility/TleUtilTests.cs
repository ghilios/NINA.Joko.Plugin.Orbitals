using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;
using SGPdotNET.TLE;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture]
    public class TleUtilTests {

        // REF: Canonical ISS (ZARYA) TLE, epoch 2008-Sep-20.
        private const string ISS_NAME = "ISS (ZARYA)";
        private const string ISS_L1 = "1 25544U 98067A   08264.51782528 -.00002182  00000-0 -11606-4 0  2927";
        private const string ISS_L2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.72125391563537";

        private static string ThreeLines() => string.Join(Environment.NewLine, ISS_NAME, ISS_L1, ISS_L2);
        private static string TwoLines() => string.Join(Environment.NewLine, ISS_L1, ISS_L2);

        [Test]
        public void ParseTle_ThreeLines_ReturnsTleWithName() {
            var tle = TleUtil.ParseTle(ThreeLines());
            tle.Should().NotBeNull();
            tle.Name.Should().Be(ISS_NAME);
        }

        [Test]
        public void ParseTle_TwoLines_ReturnsTleWithDefaultName() {
            var tle = TleUtil.ParseTle(TwoLines());
            tle.Should().NotBeNull();
            tle.Name.Should().Be("No Name");
        }

        [Test]
        public void ParseTle_OneLine_Throws() {
            Action act = () => TleUtil.ParseTle(ISS_L1);
            act.Should().Throw<ArgumentException>().WithMessage("*2 or 3 lines*");
        }

        [Test]
        public void ParseTle_FourLines_Throws() {
            Action act = () => TleUtil.ParseTle(string.Join(Environment.NewLine, ISS_NAME, ISS_L1, ISS_L2, "extra"));
            act.Should().Throw<ArgumentException>().WithMessage("*2 or 3 lines*");
        }

        [Test]
        public void TryParseTle_Valid_ReturnsTrue() {
            var ok = TleUtil.ParseTle(ThreeLines(), out Tle tle);
            ok.Should().BeTrue();
            tle.Should().NotBeNull();
        }

        [Test]
        public void TryParseTle_Invalid_ReturnsFalseAndNull() {
            var ok = TleUtil.ParseTle("garbage", out Tle tle);
            ok.Should().BeFalse();
            tle.Should().BeNull();
        }
    }
}
