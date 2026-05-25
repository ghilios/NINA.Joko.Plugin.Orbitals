using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NUnit.Framework;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Tests.SequenceItems {

    [TestFixture]
    public class SetGuiderShiftRateTests {

        private static void SetShiftRate(SetGuiderShiftRate sut, SiderealShiftTrackingRate rate) {
            // ShiftTrackingRate has a private setter; the production path is via
            // AfterParentChanged which needs a parent sequence container. Bypass via reflection.
            var prop = typeof(SetGuiderShiftRate).GetProperty(
                nameof(SetGuiderShiftRate.ShiftTrackingRate),
                BindingFlags.Public | BindingFlags.Instance);
            prop!.SetValue(sut, rate);
        }

        [Test]
        public async Task Execute_RateEnabled_CallsSetShiftRate() {
            var mediator = new Mock<IGuiderMediator>();
            mediator.Setup(m => m.SetShiftRate(It.IsAny<SiderealShiftTrackingRate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var sut = new SetGuiderShiftRate(mediator.Object);
            var rate = SiderealShiftTrackingRate.Create(raDegreesPerHour: 1.0, decDegreesPerHour: 0.5);
            SetShiftRate(sut, rate);

            await sut.Execute(progress: null, CancellationToken.None);

            mediator.Verify(m => m.SetShiftRate(rate, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Execute_RateDisabled_NoMediatorCall() {
            var mediator = new Mock<IGuiderMediator>();
            var sut = new SetGuiderShiftRate(mediator.Object);
            SetShiftRate(sut, SiderealShiftTrackingRate.Disabled);

            await sut.Execute(progress: null, CancellationToken.None);

            mediator.Verify(m => m.SetShiftRate(It.IsAny<SiderealShiftTrackingRate>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Execute_RateEnabled_SetShiftRateReturnsFalse_ThrowsSequenceEntityFailed() {
            var mediator = new Mock<IGuiderMediator>();
            mediator.Setup(m => m.SetShiftRate(It.IsAny<SiderealShiftTrackingRate>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var sut = new SetGuiderShiftRate(mediator.Object);
            SetShiftRate(sut, SiderealShiftTrackingRate.Create(1.0, 0.0));

            Func<Task> act = async () => await sut.Execute(progress: null, CancellationToken.None);
            await act.Should().ThrowAsync<SequenceEntityFailedException>();
        }
    }
}
