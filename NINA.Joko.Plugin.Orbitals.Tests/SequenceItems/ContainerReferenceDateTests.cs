using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.SequenceItems {

    /// <summary>
    /// The altitude chart takes its night shading and "Now" marker from the container's NighttimeData, which is
    /// always the current night, and its curve from the target object's reference date. When the two disagree the
    /// curve is drawn a day early and the axis stretches over two days.
    /// </summary>
    [TestFixture]
    public class ContainerReferenceDateTests {
        private const double Latitude = 45.6;
        private const double Longitude = 9.2;

        private Mock<INighttimeCalculator> nighttimeCalculatorMock;

        private SolarSystemBodyContainer CreateContainer() {
            var astrometrySettingsMock = new Mock<IAstrometrySettings>();
            astrometrySettingsMock.SetupGet(x => x.Latitude).Returns(Latitude);
            astrometrySettingsMock.SetupGet(x => x.Longitude).Returns(Longitude);
            astrometrySettingsMock.SetupGet(x => x.Horizon).Returns((CustomHorizon)null);

            var profileMock = new Mock<IProfile>();
            profileMock.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettingsMock.Object);

            var profileServiceMock = new Mock<IProfileService>();
            profileServiceMock.SetupGet(x => x.ActiveProfile).Returns(profileMock.Object);

            nighttimeCalculatorMock = new Mock<INighttimeCalculator>();

            // The reference date is what is under test, not the ephemeris
            var accessorMock = new Mock<IOrbitalElementsAccessor>();
            accessorMock
                .Setup(x => x.GetSolarSystemBodyPV(It.IsAny<DateTime>(), It.IsAny<SolarSystemBody>(), It.IsAny<Angle>(), It.IsAny<Angle>(), It.IsAny<double>(), It.IsAny<TimeSpan>()))
                .Returns(OrbitalPositionVelocity.NotSet);

            return new SolarSystemBodyContainer(
                profileServiceMock.Object,
                nighttimeCalculatorMock.Object,
                new Mock<IApplicationMediator>().Object,
                accessorMock.Object,
                new Mock<IOrbitalsOptions>().Object);
        }

        private static DateTime ReferenceDateOf(SolarSystemBodyContainer container) {
            return ((OrbitalsObjectBase)container.Target.DeepSkyObject).ReferenceDate;
        }

        private static DateTime CurrentNight => NighttimeCalculator.GetReferenceDate(DateTime.Now);

        /// <summary>
        /// Dragging a container into the sequence clones the template instance, which may have been built when NINA
        /// started on an earlier night.
        /// </summary>
        [Test]
        public void Clone_OfAContainerFromAnEarlierNight_IsOnTheCurrentNight() {
            var template = CreateContainer();
            template.Target.DeepSkyObject.SetDateAndPosition(CurrentNight.AddDays(-1), Latitude, Longitude);

            var clone = (SolarSystemBodyContainer)template.Clone();

            ReferenceDateOf(clone).Should().Be(CurrentNight);
        }

        [Test]
        public void Clone_LeavesTheSourceContainerUntouched() {
            var template = CreateContainer();
            var earlier = CurrentNight.AddDays(-1);
            template.Target.DeepSkyObject.SetDateAndPosition(earlier, Latitude, Longitude);

            template.Clone();

            ReferenceDateOf(template).Should().Be(earlier);
        }

        [Test]
        public void ReferenceDayChanged_MovesTheTargetToTheCurrentNight() {
            var container = CreateContainer();
            container.Target.DeepSkyObject.SetDateAndPosition(CurrentNight.AddDays(-1), Latitude, Longitude);

            nighttimeCalculatorMock.Raise(x => x.OnReferenceDayChanged += null, EventArgs.Empty);

            ReferenceDateOf(container).Should().Be(CurrentNight);
        }
    }
}
