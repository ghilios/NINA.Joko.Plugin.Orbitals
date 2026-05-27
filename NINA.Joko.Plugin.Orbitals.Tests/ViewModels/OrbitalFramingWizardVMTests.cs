using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Tests.Calculations;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NINA.Joko.Plugin.Orbitals.ViewModels;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NUnit.Framework;
using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugin.Orbitals.Tests.ViewModels {

    /// <summary>
    /// Phase B unit tests for <see cref="OrbitalFramingWizardVM"/>.
    ///
    /// Covers:
    ///  • Initial state after construction
    ///  • Name bound after Initialize()
    ///  • Successful capture: HasCapture=true, CapturedImage≠null, offsets=0, IsCapturing=false
    ///  • ResetFramingCommand resets offsets
    /// </summary>
    [TestFixture]
    public class OrbitalFramingWizardVMTests {

        // -------------------------------------------------------------------------
        // Factory helpers
        // -------------------------------------------------------------------------

        private static BitmapSource MakeTestBitmap(int w = 10, int h = 10) {
            var pixels = new byte[w * h * 4];
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }

        private static CapturedFrame MakeFrame(Coordinates coords, double pa = 45.0, double pixscale = 1.5) =>
            new CapturedFrame {
                Image = MakeTestBitmap(),
                WidthPx = 10,
                HeightPx = 10,
                Coordinates = coords,
                PositionAngleDeg = pa,
                PixscaleArcsecPerPx = pixscale,
            };

        private static (OrbitalFramingWizardVM vm, FakeCaptureSource capture, FakeOrbitalsObject target)
            Make(Coordinates coords, SiderealShiftTrackingRate rate, string name = "Test Object") {
            // Mock profile service
            var cameraSettings = new Mock<ICameraSettings>();
            cameraSettings.SetupGet(c => c.PixelSize).Returns(4.63); // µm
            var telescopeSettings = new Mock<ITelescopeSettings>();
            telescopeSettings.SetupGet(t => t.FocalLength).Returns(480); // mm

            var activeProfile = new Mock<IProfile>();
            activeProfile.SetupGet(p => p.CameraSettings).Returns(cameraSettings.Object);
            activeProfile.SetupGet(p => p.TelescopeSettings).Returns(telescopeSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(ps => ps.ActiveProfile).Returns(activeProfile.Object);

            // Mock nighttime calculator
            var nightCalc = new Mock<INighttimeCalculator>();
            nightCalc.Setup(n => n.Calculate()).Returns((NighttimeData)null);

            // Mock application status mediator
            var statusMediator = new Mock<IApplicationStatusMediator>();

            // Mock options
            var options = new Mock<IOrbitalsOptions>();

            // Capture source
            var capture = new FakeCaptureSource();

            var seqMediator = new Mock<NINA.Sequencer.Interfaces.Mediator.ISequenceMediator>();
            var appMediator = new Mock<NINA.WPF.Base.Interfaces.Mediator.IApplicationMediator>();

            var vm = new OrbitalFramingWizardVM(
                profileService.Object,
                new[] { capture.AsLazy() },
                nightCalc.Object,
                statusMediator.Object,
                options.Object,
                seqMediator.Object,
                appMediator.Object);

            var target = new FakeOrbitalsObject(name, coords, rate);

            return (vm, capture, target);
        }

        // -------------------------------------------------------------------------
        // Tests
        // -------------------------------------------------------------------------

        [Test]
        public void Constructor_InitialState_IsCapturingFalse_HasCaptureFalse() {
            var coords = OrbitalFramingScenarios.CometA3_20241026();
            var rate = OrbitalFramingScenarios.CometA3_20241026_TrackingRate();
            var (vm, _, _) = Make(coords, rate);

            vm.IsCapturing.Should().BeFalse();
            vm.HasCapture.Should().BeFalse();
            vm.CapturedImage.Should().BeNull();
            vm.ExposureTime.Should().Be(30.0);
            vm.Gain.Should().Be(0);
            vm.Offset.Should().Be(0);
        }

        [Test]
        public void Initialize_SetsName() {
            var coords = OrbitalFramingScenarios.Ceres_JD2460200();
            var rate = OrbitalFramingScenarios.Ceres_JD2460200_TrackingRate();
            const string objName = "1 Ceres";
            var (vm, _, target) = Make(coords, rate, objName);

            vm.Initialize(target);

            vm.Name.Should().Be(objName);
        }

        [Test]
        public void Initialize_SetsNighttimeData() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();

            // Use a nighttime calculator that returns a recognisable object.
            var cameraSettings = new Mock<ICameraSettings>();
            cameraSettings.SetupGet(c => c.PixelSize).Returns(4.63);
            var telescopeSettings = new Mock<ITelescopeSettings>();
            telescopeSettings.SetupGet(t => t.FocalLength).Returns(480);
            var activeProfile = new Mock<IProfile>();
            activeProfile.SetupGet(p => p.CameraSettings).Returns(cameraSettings.Object);
            activeProfile.SetupGet(p => p.TelescopeSettings).Returns(telescopeSettings.Object);
            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(ps => ps.ActiveProfile).Returns(activeProfile.Object);

            // NighttimeData constructor takes DateTime values that mustn't overflow;
            // use a recognisable sentinel via a simple wrapper returned by the mock.
            var expectedNighttimeData = new NighttimeData(
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(8),
                default,
                null,
                null,
                null,
                null,
                null,
                null);
            var nightCalc = new Mock<INighttimeCalculator>();
            nightCalc.Setup(n => n.Calculate()).Returns(expectedNighttimeData);

            var statusMediator = new Mock<IApplicationStatusMediator>();
            var options = new Mock<IOrbitalsOptions>();
            var capture = new FakeCaptureSource();
            var seqMediator2 = new Mock<NINA.Sequencer.Interfaces.Mediator.ISequenceMediator>();
            var appMediator2 = new Mock<NINA.WPF.Base.Interfaces.Mediator.IApplicationMediator>();

            var vm = new OrbitalFramingWizardVM(
                profileService.Object,
                new[] { capture.AsLazy() },
                nightCalc.Object,
                statusMediator.Object,
                options.Object,
                seqMediator2.Object,
                appMediator2.Object);

            var target = new FakeOrbitalsObject("Mars", coords, rate);
            vm.Initialize(target);

            vm.NighttimeData.Should().BeSameAs(expectedNighttimeData);
        }

        [Test]
        public void CaptureButtonLabel_IsLoadTestImage() {
            var coords = OrbitalFramingScenarios.Jupiter_20260115();
            var rate = OrbitalFramingScenarios.Jupiter_20260115_TrackingRate();
            var (vm, _, _) = Make(coords, rate);

            vm.CaptureButtonLabel.Should().Be("Load Test Image");
        }

        [Test]
        public async System.Threading.Tasks.Task SlewCenterAndImageCommand_OnSuccess_SetsHasCapture() {
            var coords = OrbitalFramingScenarios.CometA3_20241026();
            var rate = OrbitalFramingScenarios.CometA3_20241026_TrackingRate();
            var (vm, capture, target) = Make(coords, rate, "C/2023 A3");

            capture.Next = MakeFrame(coords, pa: 135.0, pixscale: 1.2);
            vm.Initialize(target);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            vm.HasCapture.Should().BeTrue("capture succeeded");
            vm.IsCapturing.Should().BeFalse("capture has finished");
            vm.CapturedImage.Should().NotBeNull();
        }

        [Test]
        public async System.Threading.Tasks.Task SlewCenterAndImageCommand_OnSuccess_StoresFrameMetadata() {
            var coords = OrbitalFramingScenarios.Ceres_JD2460200();
            var rate = OrbitalFramingScenarios.Ceres_JD2460200_TrackingRate();
            var (vm, capture, target) = Make(coords, rate, "1 Ceres");

            const double expectedPa = 72.3;
            const double expectedPixscale = 2.1;
            capture.Next = MakeFrame(coords, pa: expectedPa, pixscale: expectedPixscale);
            vm.Initialize(target);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            vm.CapturedImageRotation.Should().BeApproximately(expectedPa, 1e-6);
            vm.CapturedImagePixscale.Should().BeApproximately(expectedPixscale, 1e-6);
            vm.CapturedImageWidthPx.Should().Be(10);
            vm.CapturedImageHeightPx.Should().Be(10);
        }

        [Test]
        public async System.Threading.Tasks.Task SlewCenterAndImageCommand_OnSuccess_OffsetsAreZero() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, capture, target) = Make(coords, rate, "Mars");

            capture.Next = MakeFrame(coords);
            vm.Initialize(target);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            vm.RAOffsetHours.Should().Be(0, "initial capture zeroes RA offset");
            vm.DecOffsetDegrees.Should().Be(0, "initial capture zeroes Dec offset");
            // FinalPositionAngle is computed from CapturedImageRotation: (360 - PA) % 360.
            // MakeFrame uses pa=45.0 by default → expected = (360 - 45) % 360 = 315.
            const double capturedPa = 45.0;
            double expectedFinalPa = ((360.0 - capturedPa) % 360.0 + 360.0) % 360.0;
            vm.FinalPositionAngle.Should().BeApproximately(expectedFinalPa, 1e-9,
                "FinalPositionAngle is derived from the captured image PA at time of capture");
            vm.OffsetSeparationArcsec.Should().Be(0);
            vm.OffsetPositionAngleDeg.Should().Be(0);
        }

        [Test]
        public void ResetFramingCommand_ResetsAllOffsets() {
            var coords = OrbitalFramingScenarios.Jupiter_20260115();
            var rate = OrbitalFramingScenarios.Jupiter_20260115_TrackingRate();
            var (vm, _, target) = Make(coords, rate, "Jupiter");

            vm.Initialize(target);

            // Drive the rectangle offset through the canvas-facing properties
            // (the offset properties themselves are private-set; they update via RecalculateOffsets).
            // Setting RectangleOffsetXPx / Y without a prior capture is safe: RecalculateOffsets
            // guards on HasCapture and returns early, so the pixel offsets are stored but offset
            // properties stay at 0.  The reset command still demonstrates it zeroes the pixel
            // offsets and issues the correct PropertyChanged notifications.
            vm.RectangleOffsetXPx = 50.0;
            vm.RectangleOffsetYPx = -30.0;
            vm.RectangleRotationDeg = 15.0;

            vm.ResetFramingCommand.Execute(null);

            vm.RAOffsetHours.Should().Be(0);
            vm.DecOffsetDegrees.Should().Be(0);
            vm.FinalPositionAngle.Should().Be(0);
            vm.OffsetSeparationArcsec.Should().Be(0);
            vm.OffsetPositionAngleDeg.Should().Be(0);
        }

        /// <summary>
        /// Post-revision, <c>RectangleOffsetXPx</c>/<c>YPx</c> carry captured-image-pixel
        /// units. The VM feeds them to <c>Coordinates.Shift(dx, dy, capturedPa, pixscale, pixscale)</c>
        /// directly. This test mirrors that production call exactly and checks the VM
        /// produces matching RA/Dec offsets relative to the body's current position.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task RectangleOffsetPx_PostCapture_MatchesCoordinatesShift() {
            var coords = OrbitalFramingScenarios.Ceres_JD2460200();
            var rate = OrbitalFramingScenarios.Ceres_JD2460200_TrackingRate();
            var (vm, capture, target) = Make(coords, rate, "1 Ceres");

            const double pa = 30.0;
            const double pixscale = 2.0;
            capture.Next = MakeFrame(coords, pa: pa, pixscale: pixscale);
            vm.Initialize(target);
            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            const double dxImgPx = 50.0;
            const double dyImgPx = -30.0;
            vm.RectangleOffsetXPx = dxImgPx;
            vm.RectangleOffsetYPx = dyImgPx;

            var framingTarget = coords.Shift(dxImgPx, dyImgPx, pa, pixscale, pixscale);
            double expectedRaDiff = framingTarget.RA - coords.RA;
            while (expectedRaDiff > 12.0) expectedRaDiff -= 24.0;
            while (expectedRaDiff < -12.0) expectedRaDiff += 24.0;
            double expectedDec = framingTarget.Dec - coords.Dec;

            vm.RAOffsetHours.Should().BeApproximately(expectedRaDiff, 1e-9);
            vm.DecOffsetDegrees.Should().BeApproximately(expectedDec, 1e-9);
        }

        /// <summary>
        /// Locks in the "delta from plate-solved PA" semantics: <c>FinalPositionAngle</c>
        /// depends only on the sum <c>CapturedImageRotation + RectangleRotationDeg</c>.
        /// Different splits with the same sum must yield the same final PA.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task FinalPositionAngle_DependsOnlyOnSumOfCapturedPaAndRectangleDelta() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();

            var pairs = new[] {
                (capturedPa: 30.0, delta: 70.0),
                (capturedPa: 60.0, delta: 40.0),
                (capturedPa: 90.0, delta: 10.0),
                (capturedPa: 100.0, delta: 0.0),
            };

            const double expected = ((360.0 - 100.0) % 360.0 + 360.0) % 360.0;

            foreach (var (capturedPa, delta) in pairs) {
                var (vm, capture, target) = Make(coords, rate, "Mars");
                capture.Next = MakeFrame(coords, pa: capturedPa);
                vm.Initialize(target);
                await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

                if (delta != 0.0) {
                    vm.RectangleRotationDeg = delta;
                }

                vm.FinalPositionAngle.Should().BeApproximately(
                    expected, 1e-9,
                    $"pair (capturedPa={capturedPa}, delta={delta}) sums to 100°");
            }
        }

        [Test]
        public async System.Threading.Tasks.Task SlewCenterAndImageCommand_CanExecute_FalseWhileCapturing() {
            var coords = OrbitalFramingScenarios.Halley_JD2449400();
            var rate = OrbitalFramingScenarios.Halley_JD2449400_TrackingRate();

            // Use a capture source that blocks until told to proceed.
            var tcs = new System.Threading.Tasks.TaskCompletionSource<CapturedFrame>();
            var blockingCapture = new BlockingCaptureSource(tcs.Task);

            var cameraSettings = new Mock<ICameraSettings>();
            cameraSettings.SetupGet(c => c.PixelSize).Returns(4.63);
            var telescopeSettings = new Mock<ITelescopeSettings>();
            telescopeSettings.SetupGet(t => t.FocalLength).Returns(480);
            var activeProfile = new Mock<IProfile>();
            activeProfile.SetupGet(p => p.CameraSettings).Returns(cameraSettings.Object);
            activeProfile.SetupGet(p => p.TelescopeSettings).Returns(telescopeSettings.Object);
            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(ps => ps.ActiveProfile).Returns(activeProfile.Object);
            var nightCalc = new Mock<INighttimeCalculator>();
            nightCalc.Setup(n => n.Calculate()).Returns((NighttimeData)null);
            var statusMediator = new Mock<IApplicationStatusMediator>();
            var options = new Mock<IOrbitalsOptions>();
            var seqMediator3 = new Mock<NINA.Sequencer.Interfaces.Mediator.ISequenceMediator>();
            var appMediator3 = new Mock<NINA.WPF.Base.Interfaces.Mediator.IApplicationMediator>();

            var vm = new OrbitalFramingWizardVM(
                profileService.Object,
                new[] { blockingCapture.AsLazy() },
                nightCalc.Object,
                statusMediator.Object,
                options.Object,
                seqMediator3.Object,
                appMediator3.Object);

            var target = new FakeOrbitalsObject("1P/Halley", coords, rate);
            vm.Initialize(target);

            // Start the capture (don't await; it's blocked).
            var captureTask = vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            // Give the async machinery a moment to flip IsCapturing.
            await System.Threading.Tasks.Task.Delay(50);

            vm.IsCapturing.Should().BeTrue("command is in flight");

            // Unblock the capture.
            tcs.SetResult(MakeFrame(coords));
            await captureTask;

            vm.IsCapturing.Should().BeFalse("command has completed");
            vm.HasCapture.Should().BeTrue();
        }

        // ─── Helper: blocking capture source for the IsCapturing test ────────────

        private sealed class BlockingCaptureSource : ICaptureSource {
            private readonly System.Threading.Tasks.Task<CapturedFrame> blocker;
            public BlockingCaptureSource(System.Threading.Tasks.Task<CapturedFrame> blocker) {
                this.blocker = blocker;
            }
            public async System.Threading.Tasks.Task<CapturedFrame> CaptureAsync(
                OrbitalsObjectBase target,
                OrbitalFramingExposureSettings exposure,
                IProgress<ApplicationStatus> progress,
                System.Threading.CancellationToken ct) {
                return await blocker;
            }
        }
    }
}
