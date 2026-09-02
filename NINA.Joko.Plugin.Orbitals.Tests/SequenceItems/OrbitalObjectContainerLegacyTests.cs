using FluentAssertions;
using Moq;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NUnit.Framework;

namespace NINA.Joko.Plugin.Orbitals.Tests.SequenceItems {

    [TestFixture]
    public class OrbitalObjectContainerLegacyTests {

        private static OrbitalObjectContainer CreateContainer() {
            var astrometrySettingsMock = new Mock<IAstrometrySettings>();
            astrometrySettingsMock.SetupGet(x => x.Latitude).Returns(45.6);
            astrometrySettingsMock.SetupGet(x => x.Longitude).Returns(9.2);
            astrometrySettingsMock.SetupGet(x => x.Horizon).Returns((CustomHorizon)null);

            var profileMock = new Mock<IProfile>();
            profileMock.SetupGet(x => x.AstrometrySettings).Returns(astrometrySettingsMock.Object);

            var profileServiceMock = new Mock<IProfileService>();
            profileServiceMock.SetupGet(x => x.ActiveProfile).Returns(profileMock.Object);

            return new OrbitalObjectContainer(
                profileServiceMock.Object,
                new Mock<INighttimeCalculator>().Object,
                new Mock<IApplicationMediator>().Object,
                new Mock<IOrbitalElementsAccessor>().Object,
                new Mock<IOrbitalsOptions>().Object);
        }

        /// <summary>
        /// Comet was the only member of <see cref="OrbitalObjectTypeEnum"/> when the plugin shipped,
        /// so it took the implicit value 0. Adding the asteroid types renumbered it to 1, which left
        /// every sequence, target and template saved before that carrying an object type the enum no
        /// longer defines. Json.NET does not range check enums, so 0 lands on this property untouched
        /// and is later used to index the orbital elements backend, throwing KeyNotFoundException.
        /// </summary>
        [Test]
        public void ObjectType_SetToTheValueSavedBeforeAsteroidsExisted_BecomesComet() {
            var container = CreateContainer();

            container.ObjectType = (OrbitalObjectTypeEnum)0;

            container.ObjectType.Should().Be(OrbitalObjectTypeEnum.Comet);
        }

        [Test]
        public void ObjectType_SetToADefinedValue_IsKept() {
            var container = CreateContainer();

            container.ObjectType = OrbitalObjectTypeEnum.NumberedAsteroids;

            container.ObjectType.Should().Be(OrbitalObjectTypeEnum.NumberedAsteroids);
        }
    }
}
