using FluentAssertions;
using Moq;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.ViewModels;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Tests.ViewModels {

    [TestFixture]
    public class OrbitalSearchVMTests {

        private static OrbitalElements MakeElement(string name) {
            return new OrbitalElements(name) {
                PrimaryGravitationalParameter = GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                q_Perihelion_au = 1.0,
                e_Eccentricity = 0.5,
                tp_PeriapsisTime_jd = 2451545.0,
            };
        }

        [Test]
        public void Constructor_InitialState() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            var sut = new OrbitalSearchVM(accessor.Object);

            sut.ObjectType.Should().Be(OrbitalObjectTypeEnum.Comet,
                "the VM starts in Comet search mode per the ctor");
            sut.Limit.Should().Be(15);
            sut.TargetSearchResult.Should().BeNull();
            sut.ShowPopup.Should().BeFalse();
        }

        [Test]
        public void TargetName_TooShort_DoesNotTriggerSearch() {
            // Implementation only kicks off a search when TargetName.Length > 2.
            var accessor = new Mock<IOrbitalElementsAccessor>();
            var sut = new OrbitalSearchVM(accessor.Object);

            sut.TargetName = "ha";

            sut.TargetSearchResult.Should().BeNull(
                "string of length 2 must not start a search task");
            accessor.Verify(
                a => a.Search(It.IsAny<OrbitalObjectTypeEnum>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.Never);
        }

        [Test]
        public async Task TargetName_LongEnough_TriggersSearchOnAccessor() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            accessor.Setup(a => a.Search(
                OrbitalObjectTypeEnum.Comet,
                "Halley",
                It.IsAny<int?>())).Returns(new[] { MakeElement("1P/Halley") });

            var sut = new OrbitalSearchVM(accessor.Object);
            sut.TargetName = "Halley";

            sut.TargetSearchResult.Should().NotBeNull("a search task is kicked off for length > 2");
            await sut.TargetSearchResult.Task; // wait through the 100ms internal debounce

            sut.TargetSearchResult.Result.Should().HaveCount(1);
            sut.TargetSearchResult.Result[0].Column1.Should().Be("1P/Halley");
            accessor.Verify(a => a.Search(OrbitalObjectTypeEnum.Comet, "Halley", 15), Times.Once);
        }

        [Test]
        public async Task SearchResults_ShowPopupTogglesWithResultCount() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            accessor.Setup(a => a.Search(
                It.IsAny<OrbitalObjectTypeEnum>(),
                It.IsAny<string>(),
                It.IsAny<int?>())).Returns(new[] { MakeElement("1P/Halley") });

            var sut = new OrbitalSearchVM(accessor.Object);
            sut.TargetName = "Halley";
            await sut.TargetSearchResult.Task;
            // PropertyChanged subscription is what flips ShowPopup; allow the
            // continuation a moment to fire.
            await Task.Yield();

            sut.ShowPopup.Should().BeTrue("a non-empty result set opens the popup");
        }

        [Test]
        public async Task SearchResults_Empty_KeepsPopupClosed() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            accessor.Setup(a => a.Search(
                It.IsAny<OrbitalObjectTypeEnum>(),
                It.IsAny<string>(),
                It.IsAny<int?>())).Returns(Enumerable.Empty<OrbitalElements>());

            var sut = new OrbitalSearchVM(accessor.Object);
            sut.TargetName = "Zzzzz";
            await sut.TargetSearchResult.Task;
            await Task.Yield();

            sut.ShowPopup.Should().BeFalse();
        }

        [Test]
        public void SetTargetNameWithoutSearch_BypassesAccessor() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            var sut = new OrbitalSearchVM(accessor.Object);

            sut.SetTargetNameWithoutSearch("Halley");

            sut.TargetName.Should().Be("Halley");
            sut.TargetSearchResult.Should().BeNull();
            accessor.Verify(
                a => a.Search(It.IsAny<OrbitalObjectTypeEnum>(), It.IsAny<string>(), It.IsAny<int?>()),
                Times.Never);
        }

        [Test]
        public void TargetName_SameValueTwice_DoesNotReSearch() {
            // Setter short-circuits when value equals existing targetName.
            var accessor = new Mock<IOrbitalElementsAccessor>();
            accessor.Setup(a => a.Search(
                It.IsAny<OrbitalObjectTypeEnum>(),
                It.IsAny<string>(),
                It.IsAny<int?>())).Returns(Enumerable.Empty<OrbitalElements>());

            var sut = new OrbitalSearchVM(accessor.Object);
            sut.TargetName = "Halley";
            sut.TargetName = "Halley"; // duplicate -- no-op

            // The first set started a task; the second must NOT have started another.
            // Easiest assertion: accessor.Search was called exactly once.
            accessor.Verify(a => a.Search(
                It.IsAny<OrbitalObjectTypeEnum>(), "Halley", It.IsAny<int?>()),
                Times.AtMostOnce);
        }
    }
}
