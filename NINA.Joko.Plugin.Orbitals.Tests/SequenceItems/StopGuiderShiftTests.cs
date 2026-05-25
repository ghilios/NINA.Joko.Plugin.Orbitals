using FluentAssertions;
using Moq;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Tests.SequenceItems {

    [TestFixture]
    public class StopGuiderShiftTests {

        [Test]
        public async Task Execute_GuiderStopShiftingReturnsTrue_NoException() {
            var mediator = new Mock<IGuiderMediator>();
            mediator.Setup(m => m.StopShifting(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var sut = new StopGuiderShift(mediator.Object);

            Func<Task> act = async () => await sut.Execute(progress: null, CancellationToken.None);

            await act.Should().NotThrowAsync();
            mediator.Verify(m => m.StopShifting(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Execute_GuiderStopShiftingReturnsFalse_ThrowsSequenceEntityFailed() {
            var mediator = new Mock<IGuiderMediator>();
            mediator.Setup(m => m.StopShifting(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var sut = new StopGuiderShift(mediator.Object);

            Func<Task> act = async () => await sut.Execute(progress: null, CancellationToken.None);

            await act.Should().ThrowAsync<SequenceEntityFailedException>();
        }

        [Test]
        public void Clone_PreservesGuiderMediatorReference() {
            var mediator = new Mock<IGuiderMediator>();
            var original = new StopGuiderShift(mediator.Object);
            var clone = (StopGuiderShift)original.Clone();

            // Verify the cloned instance still calls the same mediator.
            clone.Should().NotBeSameAs(original);
            mediator.Setup(m => m.StopShifting(It.IsAny<CancellationToken>())).ReturnsAsync(true);
            clone.Invoking(c => c.Execute(null, CancellationToken.None).GetAwaiter().GetResult())
                .Should().NotThrow();
            mediator.Verify(m => m.StopShifting(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
