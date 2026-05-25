using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Tests.SequenceItems {

    [TestFixture]
    public class SetTelescopeShiftRateTests {

        private static void InjectOptions(SetTelescopeShiftRate sut, IOrbitalsOptions options) {
            // 'options' is a private readonly field initialized from
            // OrbitalsPlugin.OrbitalsOptions during construction (which is null
            // outside of the NINA host). Override it directly to a mock.
            var field = typeof(SetTelescopeShiftRate).GetField("options",
                BindingFlags.Instance | BindingFlags.NonPublic);
            field!.SetValue(sut, options);
        }

        private static void SetShiftRate(SetTelescopeShiftRate sut, SiderealShiftTrackingRate rate) {
            var prop = typeof(SetTelescopeShiftRate).GetProperty(
                nameof(SetTelescopeShiftRate.ShiftTrackingRate),
                BindingFlags.Public | BindingFlags.Instance);
            prop!.SetValue(sut, rate);
        }

        private static (SetTelescopeShiftRate sut, Mock<ITelescopeMediator> telescope, Mock<IOrbitalsOptions> options) Make() {
            var telescope = new Mock<ITelescopeMediator>();
            var options = new Mock<IOrbitalsOptions>();
            options.SetupGet(o => o.QuirksMode).Returns(QuirksModeEnum.None);
            var sut = new SetTelescopeShiftRate(telescope.Object);
            InjectOptions(sut, options.Object);
            return (sut, telescope, options);
        }

        [Test]
        public async Task Execute_RateEnabled_CallsSetCustomTrackingRate_AppliesQuirks() {
            var (sut, telescope, options) = Make();
            options.SetupGet(o => o.QuirksMode).Returns(QuirksModeEnum.EQMOD);

            var rate = SiderealShiftTrackingRate.Create(raDegreesPerHour: 1.0, decDegreesPerHour: 0.5);
            SetShiftRate(sut, rate);

            telescope.Setup(t => t.SetCustomTrackingRate(It.IsAny<SiderealShiftTrackingRate>())).Returns(true);

            await sut.Execute(progress: null, CancellationToken.None);

            // Verify SetCustomTrackingRate was called with a rate whose RA has been
            // scaled by the EQMOD quirks factor. We don't recompute it from the impl;
            // we assert that RA is NOT equal to the input (it must have been scaled).
            telescope.Verify(t => t.SetCustomTrackingRate(It.Is<SiderealShiftTrackingRate>(
                r => Math.Abs(r.RADegreesPerHour - 1.0) > 1e-6
                  && Math.Abs(r.DecDegreesPerHour - 0.5) < 1e-9)),
                Times.Once);
        }

        [Test]
        public async Task Execute_RateEnabled_QuirksModeNone_DoesNotAlterRate() {
            var (sut, telescope, _) = Make();
            var rate = SiderealShiftTrackingRate.Create(raDegreesPerHour: 1.0, decDegreesPerHour: 0.5);
            SetShiftRate(sut, rate);
            telescope.Setup(t => t.SetCustomTrackingRate(It.IsAny<SiderealShiftTrackingRate>())).Returns(true);

            await sut.Execute(progress: null, CancellationToken.None);

            telescope.Verify(t => t.SetCustomTrackingRate(It.Is<SiderealShiftTrackingRate>(
                r => Math.Abs(r.RADegreesPerHour - 1.0) < 1e-9)),
                Times.Once);
        }

        [Test]
        public async Task Execute_RateDisabled_CallsSetTrackingMode_Sidereal() {
            var (sut, telescope, _) = Make();
            SetShiftRate(sut, SiderealShiftTrackingRate.Disabled);
            telescope.Setup(t => t.SetTrackingMode(TrackingMode.Sidereal)).Returns(true);

            await sut.Execute(progress: null, CancellationToken.None);

            telescope.Verify(t => t.SetTrackingMode(TrackingMode.Sidereal), Times.Once);
        }

        [Test]
        public async Task Execute_SetCustomTrackingRateFails_ThrowsSequenceEntityFailed() {
            var (sut, telescope, _) = Make();
            SetShiftRate(sut, SiderealShiftTrackingRate.Create(1.0, 0.0));
            telescope.Setup(t => t.SetCustomTrackingRate(It.IsAny<SiderealShiftTrackingRate>())).Returns(false);

            Func<Task> act = async () => await sut.Execute(progress: null, CancellationToken.None);
            await act.Should().ThrowAsync<SequenceEntityFailedException>();
        }

        [Test]
        public async Task Execute_RateDisabled_SetTrackingModeFails_ThrowsSequenceEntityFailed() {
            var (sut, telescope, _) = Make();
            SetShiftRate(sut, SiderealShiftTrackingRate.Disabled);
            telescope.Setup(t => t.SetTrackingMode(TrackingMode.Sidereal)).Returns(false);

            Func<Task> act = async () => await sut.Execute(progress: null, CancellationToken.None);
            await act.Should().ThrowAsync<SequenceEntityFailedException>();
        }
    }
}
