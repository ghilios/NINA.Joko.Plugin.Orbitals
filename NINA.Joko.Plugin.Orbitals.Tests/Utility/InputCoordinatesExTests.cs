using FluentAssertions;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture]
    public class InputCoordinatesExTests {

        // REF: ICRS catalog positions, used here only for human-recognizable numbers.
        //   Sirius: RA 06h 45m 08.917s, Dec -16d 42' 58.02"
        //   Polaris: RA 02h 31m 49.09s, Dec +89d 15' 50.79"

        private const double SiriusRAHours = 6.0 + 45.0 / 60.0 + 8.917 / 3600.0;
        private const double SiriusDecDeg = -(16.0 + 42.0 / 60.0 + 58.02 / 3600.0);

        private const double PolarisRAHours = 2.0 + 31.0 / 60.0 + 49.09 / 3600.0;
        private const double PolarisDecDeg = 89.0 + 15.0 / 60.0 + 50.79 / 3600.0;

        private static InputCoordinatesEx Make(double raHours, double decDeg) {
            return new InputCoordinatesEx(new Coordinates(
                Angle.ByHours(raHours),
                Angle.ByDegree(decDeg),
                Epoch.J2000));
        }

        [Test]
        public void Sirius_DecomposesIntoHMS_DMS() {
            var sut = Make(SiriusRAHours, SiriusDecDeg);

            sut.RAHours.Should().Be(6);
            sut.RAMinutes.Should().Be(45);
            sut.RASeconds.Should().BeApproximately(8.917, 1e-5);

            sut.NegativeDec.Should().BeTrue();
            sut.DecDegrees.Should().Be(-16);
            sut.DecMinutes.Should().Be(42);
            sut.DecSeconds.Should().BeApproximately(58.02, 1e-5);
        }

        [Test]
        public void Polaris_DecomposesIntoHMS_DMS_NearPole() {
            var sut = Make(PolarisRAHours, PolarisDecDeg);

            sut.RAHours.Should().Be(2);
            sut.RAMinutes.Should().Be(31);
            sut.RASeconds.Should().BeApproximately(49.09, 1e-5);

            sut.NegativeDec.Should().BeFalse();
            sut.DecDegrees.Should().Be(89);
            sut.DecMinutes.Should().Be(15);
            sut.DecSeconds.Should().BeApproximately(50.79, 1e-5);
        }

        [Test]
        public void ZeroCoordinates_LeavesNegativeFlagsFalse() {
            // RaiseCoordinatesChanged short-circuits on (0,0) and does not flip the
            // negative flags. Pin this so a future change to the guard is deliberate.
            var sut = Make(0.0, 0.0);
            sut.NegativeRA.Should().BeFalse();
            sut.NegativeDec.Should().BeFalse();
            sut.RAHours.Should().Be(0);
            sut.RAMinutes.Should().Be(0);
            sut.RASeconds.Should().Be(0.0);
            sut.DecDegrees.Should().Be(0);
            sut.DecMinutes.Should().Be(0);
            sut.DecSeconds.Should().Be(0.0);
        }

        [Test]
        public void DecPositiveNinety_DecomposesCleanly() {
            var sut = Make(0.0, 90.0);

            sut.DecDegrees.Should().Be(90);
            sut.DecMinutes.Should().Be(0);
            sut.DecSeconds.Should().Be(0.0);
            sut.NegativeDec.Should().BeFalse();
        }

        [Test]
        public void DecNegativeNinety_DecomposesCleanly() {
            var sut = Make(0.0, -90.0);

            sut.DecDegrees.Should().Be(-90);
            sut.DecMinutes.Should().Be(0);
            sut.DecSeconds.Should().Be(0.0);
            sut.NegativeDec.Should().BeTrue();
        }

        [Test]
        public void RAJustBelow24Hours_StaysAt23h() {
            // 23h 59m 59s = 23 + 59/60 + 59/3600 = 23.9997222... hours
            var raHours = 23.0 + 59.0 / 60.0 + 59.0 / 3600.0;
            var sut = Make(raHours, 0.0);

            sut.RAHours.Should().Be(23);
            sut.RAMinutes.Should().Be(59);
            sut.RASeconds.Should().BeApproximately(59.0, 1e-5);
        }

        [Test]
        public void NegativeRA_SetsNegativeRAFlag() {
            // The implementation auto-toggles NegativeRA from sign when Coordinates change.
            // Note: in astronomy RA is typically [0, 24), so a negative RA here is a
            // synthetic case meant only to verify the sign-tracking flag behaves.
            var sut = Make(-1.0, 0.0);
            sut.NegativeRA.Should().BeTrue();
        }

        [Test]
        public void CoordinatesChanged_FiresWhenCoordinatesPropertySet() {
            var sut = Make(0.0, 0.0);
            var fired = 0;
            sut.CoordinatesChanged += (_, _) => fired++;

            sut.Coordinates = new Coordinates(Angle.ByHours(5.0), Angle.ByDegree(10.0), Epoch.J2000);

            fired.Should().Be(1);
        }

        [Test]
        public void RoundTrip_SetFieldsThenReadCoordinates_ReproducesSirius() {
            // Start at (0, 0). Setting NegativeDec=true then DecDegrees=-16 etc.
            // exercises the setter path the WPF view uses.
            var sut = Make(0.0, 0.0);

            sut.RAHours = 6;
            sut.RAMinutes = 45;
            sut.RASeconds = 8.917;
            sut.NegativeDec = true;
            sut.DecDegrees = -16;
            sut.DecMinutes = 42;
            sut.DecSeconds = 58.02;

            sut.Coordinates.RA.Should().BeApproximately(SiriusRAHours, 1e-9);
            sut.Coordinates.Dec.Should().BeApproximately(SiriusDecDeg, 1e-9);
        }
    }
}
