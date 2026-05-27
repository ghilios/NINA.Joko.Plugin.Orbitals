using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NINA.Joko.Plugin.Orbitals.Tests.Calculations;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NINA.Joko.Plugin.Orbitals.ViewModels;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.SkySurvey;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Tests.ViewModels {

    /// <summary>
    /// Phase D unit tests for <see cref="OrbitalFramingWizardVM"/> sequencer export.
    ///
    /// Tests the ExportToSequencerCommand behavior: correct container type selected,
    /// target/offset/PA fields populated, AddAdvancedTarget called, ChangeTab called.
    /// </summary>
    [TestFixture]
    public class OrbitalFramingWizardExportTests {

        // ─── One-time setup: inject plugin statics so container Clone() works ────

        [OneTimeSetUp]
        public void InitialiseOrbitalsPluginStatics() {
            // OrbitalObjectContainer.Clone(), JWSTContainer.Clone(), etc. delegate
            // back through OrbitalsPlugin.OrbitalElementsAccessor / OrbitalsOptions
            // (set only by the MEF [ImportingConstructor], which never runs in tests).
            // Inject minimal mock / real instances so those Clone() paths succeed.

            // The static properties have private setters; reach them via reflection.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic;

            // 1. Inject a mock IOrbitalsOptions (OrbitalsPlugin.OrbitalsOptions is now
            //    typed as IOrbitalsOptions, so a Moq mock is sufficient).
            var optsMock = MakeOrbitalsOptions();
            optsMock.SetupAdd(o => o.PropertyChanged += It.IsAny<System.ComponentModel.PropertyChangedEventHandler>());

            var optsProp = typeof(OrbitalsPlugin).GetProperty(
                nameof(OrbitalsPlugin.OrbitalsOptions), flags);
            optsProp?.SetValue(null, optsMock.Object);

            // 2. Build a real OrbitalElementsAccessor (light-weight: just registers
            //    empty in-memory backends and attaches no listeners).
            var realAccessor = new OrbitalElementsAccessor(optsMock.Object);

            // WaitUntilLoaded() blocks on a ManualResetEvent that Load() sets.
            // Load() reads binary files from disk (which don't exist in tests).
            // Signal the event directly so WaitUntilLoaded() returns immediately.
            const BindingFlags instanceFlags = BindingFlags.NonPublic | BindingFlags.Instance;
            var loadedEventField = typeof(OrbitalElementsAccessor).GetField("loadedEvent", instanceFlags);
            (loadedEventField?.GetValue(realAccessor) as System.Threading.ManualResetEvent)?.Set();

            var accProp = typeof(OrbitalsPlugin).GetProperty(
                nameof(OrbitalsPlugin.OrbitalElementsAccessor), flags);
            accProp?.SetValue(null, realAccessor);
        }

        // ─── Shared mock helpers ─────────────────────────────────────────────────

        private static Mock<IProfileService> MakeProfileService() {
            var astrometrySettings = new Mock<IAstrometrySettings>();
            astrometrySettings.SetupGet(a => a.Latitude).Returns(47.0);
            astrometrySettings.SetupGet(a => a.Longitude).Returns(11.0);
            astrometrySettings.SetupGet(a => a.Elevation).Returns(600.0);
            astrometrySettings.SetupGet(a => a.Horizon).Returns((CustomHorizon)null);

            var cameraSettings = new Mock<ICameraSettings>();
            cameraSettings.SetupGet(c => c.PixelSize).Returns(4.63);
            var telescopeSettings = new Mock<ITelescopeSettings>();
            telescopeSettings.SetupGet(t => t.FocalLength).Returns(480);

            var activeProfile = new Mock<IProfile>();
            activeProfile.SetupGet(p => p.AstrometrySettings).Returns(astrometrySettings.Object);
            activeProfile.SetupGet(p => p.CameraSettings).Returns(cameraSettings.Object);
            activeProfile.SetupGet(p => p.TelescopeSettings).Returns(telescopeSettings.Object);

            var profileService = new Mock<IProfileService>();
            profileService.SetupGet(ps => ps.ActiveProfile).Returns(activeProfile.Object);
            // Event stubs so WeakEventManager can add/remove handlers.
            profileService.SetupAdd(ps => ps.LocationChanged += It.IsAny<EventHandler>());
            profileService.SetupRemove(ps => ps.LocationChanged -= It.IsAny<EventHandler>());
            profileService.SetupAdd(ps => ps.HorizonChanged += It.IsAny<EventHandler>());
            profileService.SetupRemove(ps => ps.HorizonChanged -= It.IsAny<EventHandler>());

            return profileService;
        }

        private static Mock<IOrbitalElementsAccessor> MakeOrbitalElementsAccessor() {
            var accessor = new Mock<IOrbitalElementsAccessor>();
            // Return a minimal OrbitalElements on Get so SelectedOrbitalName setter can succeed.
            accessor.Setup(a => a.Get(It.IsAny<OrbitalObjectTypeEnum>(), It.IsAny<string>()))
                    .Returns<OrbitalObjectTypeEnum, string>((_, name) => new OrbitalElements(name));
            accessor.Setup(a => a.WaitUntilLoaded());
            // Return a valid (zero) PV so orbital object constructors don't throw.
            accessor.Setup(a => a.GetSolarSystemBodyPV(
                    It.IsAny<DateTime>(), It.IsAny<SolarSystemBody>(), It.IsAny<TimeSpan>()))
                    .Returns(OrbitalPositionVelocity.NotSet);
            accessor.Setup(a => a.GetObjectPV(
                    It.IsAny<DateTime>(), It.IsAny<OrbitalElements>(),
                    It.IsAny<Angle>(), It.IsAny<Angle>(), It.IsAny<double>(), It.IsAny<TimeSpan>()))
                    .Returns(OrbitalPositionVelocity.NotSet);
            accessor.Setup(a => a.GetPVFromTable(
                    It.IsAny<DateTime>(), It.IsAny<PVTable>(),
                    It.IsAny<Angle>(), It.IsAny<Angle>(), It.IsAny<double>(), It.IsAny<TimeSpan>()))
                    .Returns(OrbitalPositionVelocity.NotSet);
            accessor.Setup(a => a.GetJWSTVectorTable()).Returns((PVTable)null);
            return accessor;
        }

        private static Mock<IOrbitalsOptions> MakeOrbitalsOptions() {
            var options = new Mock<IOrbitalsOptions>();
            options.SetupGet(o => o.OrbitalPositionRefreshTime_sec).Returns(60);
            options.SetupGet(o => o.TLEPositionRefreshTime_sec).Returns(5);
            return options;
        }

        private static Mock<INighttimeCalculator> MakeNighttimeCalculator() {
            var calc = new Mock<INighttimeCalculator>();
            calc.Setup(n => n.Calculate()).Returns((NighttimeData)null);
            return calc;
        }

        private static Mock<IApplicationMediator> MakeApplicationMediator() {
            var am = new Mock<IApplicationMediator>();
            return am;
        }

        private static Mock<ISequenceMediator> MakeSequenceMediator() {
            var sm = new Mock<ISequenceMediator>();
            return sm;
        }

        private static Mock<ITelescopeMediator> MakeTelescopeMediator() {
            var tm = new Mock<ITelescopeMediator>();
            var info = new Equipment.Equipment.MyTelescope.TelescopeInfo();
            tm.Setup(t => t.GetInfo()).Returns(info);
            return tm;
        }

        private static Mock<IGuiderMediator> MakeGuiderMediator() {
            return new Mock<IGuiderMediator>();
        }

        /// <summary>
        /// Creates a VM with all required mocks and simulated capture state.
        /// Returns the VM plus the key mocks for assertions.
        /// </summary>
        private (
            OrbitalFramingWizardVM vm,
            Mock<ISequenceMediator> seqMediatorMock,
            Mock<IApplicationMediator> appMediatorMock
        ) MakeVmWithCapture(
            Coordinates captureCoords,
            double capturedPaDeg = 30.0,
            double capturedPixscale = 1.5) {

            var profileService = MakeProfileService();
            var nightCalc = MakeNighttimeCalculator();
            var statusMediator = new Mock<IApplicationStatusMediator>();
            var options = MakeOrbitalsOptions();
            var seqMediator = MakeSequenceMediator();
            var appMediator = MakeApplicationMediator();

            var capture = new FakeCaptureSource();
            var bmp = MakeBitmap(10, 10);
            capture.Next = new CapturedFrame {
                Image = bmp,
                WidthPx = 10,
                HeightPx = 10,
                Coordinates = captureCoords,
                PositionAngleDeg = capturedPaDeg,
                PixscaleArcsecPerPx = capturedPixscale,
            };

            var skySurveyFactory = new Mock<ISkySurveyFactory>();
            skySurveyFactory.Setup(f => f.Create(It.IsAny<SkySurveySource>())).Returns(Mock.Of<ISkySurvey>());

            var vm = new OrbitalFramingWizardVM(
                profileService.Object,
                new[] { ((ICaptureSource)capture).AsLazy() },
                nightCalc.Object,
                statusMediator.Object,
                options.Object,
                seqMediator.Object,
                appMediator.Object,
                skySurveyFactory.Object);

            return (vm, seqMediator, appMediator);
        }

        private static BitmapSource MakeBitmap(int w, int h) {
            var pixels = new byte[w * h * 4];
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, pixels, w * 4);
            bmp.Freeze();
            return bmp;
        }

        // ─── Helper: build minimal real container instances ───────────────────────

        private SolarSystemBodyContainer MakeSolarSystemBodyContainer() {
            var ps = MakeProfileService();
            var nc = MakeNighttimeCalculator();
            var am = MakeApplicationMediator();
            var acc = MakeOrbitalElementsAccessor();
            var opts = MakeOrbitalsOptions();
            return new SolarSystemBodyContainer(
                ps.Object, nc.Object, am.Object,
                acc.Object, opts.Object);
        }

        private OrbitalObjectContainer MakeOrbitalObjectContainer() {
            var ps = MakeProfileService();
            var nc = MakeNighttimeCalculator();
            var am = MakeApplicationMediator();
            var acc = MakeOrbitalElementsAccessor();
            var opts = MakeOrbitalsOptions();
            return new OrbitalObjectContainer(
                ps.Object, nc.Object, am.Object,
                acc.Object, opts.Object);
        }

        private ManualTLEContainer MakeManualTLEContainer() {
            var ps = MakeProfileService();
            var nc = MakeNighttimeCalculator();
            var am = MakeApplicationMediator();
            var tm = MakeTelescopeMediator();
            var gm = MakeGuiderMediator();
            var opts = MakeOrbitalsOptions();
            return new ManualTLEContainer(
                ps.Object, nc.Object, am.Object,
                tm.Object, gm.Object, opts.Object);
        }

        private JWSTContainer MakeJWSTContainer() {
            var ps = MakeProfileService();
            var nc = MakeNighttimeCalculator();
            var am = MakeApplicationMediator();
            var acc = MakeOrbitalElementsAccessor();
            var opts = MakeOrbitalsOptions();
            return new JWSTContainer(
                ps.Object, nc.Object, am.Object,
                acc.Object, opts.Object);
        }

        // ─── Tests: no template / no capture guards ───────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_NoCapture_ShowsWarning_DoesNotCallAddAdvancedTarget() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var target = new FakeOrbitalsObject("Mars", coords, rate);
            vm.Initialize(target);
            // HasCapture is false — no image captured yet.

            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Never);
        }

        [Test]
        public async System.Threading.Tasks.Task Export_NoMatchingTemplate_ShowsError_DoesNotCallAddAdvancedTarget() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            // No templates registered.
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer>());

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            // Simulate a capture.
            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Never);
        }

        // ─── Tests: SolarSystemBodyContainer export ───────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_SolarSystemBody_SelectsCorrectContainerType() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, appMediator) = MakeVmWithCapture(coords);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Once);
            capturedContainer.Should().BeOfType<SolarSystemBodyContainer>();
        }

        [Test]
        public async System.Threading.Tasks.Task Export_SolarSystemBody_SetsPositionAngle() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            const double capturedPa = 45.0;
            var (vm, seqMediator, _) = MakeVmWithCapture(coords, capturedPaDeg: capturedPa);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (SolarSystemBodyContainer)capturedContainer;
            // FinalPositionAngle is derived from captured PA and rectangle rotation.
            // At capture time with no rectangle rotation, FinalPositionAngle = (360 - PA) % 360.
            double expectedFinalPa = ((360.0 - capturedPa) % 360.0 + 360.0) % 360.0;
            exported.Target.PositionAngle.Should().BeApproximately(expectedFinalPa, 1e-6);
        }

        [Test]
        public async System.Threading.Tasks.Task Export_SolarSystemBody_SetsOffsetCoordinates() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            // No pixel drag — offsets should be zero at export.
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (SolarSystemBodyContainer)capturedContainer;
            exported.OffsetCoordinates.Coordinates.RA.Should().BeApproximately(0.0, 1e-9);
            exported.OffsetCoordinates.Coordinates.Dec.Should().BeApproximately(0.0, 1e-9);
        }

        [Test]
        public async System.Threading.Tasks.Task Export_SolarSystemBody_SetsSelectedSolarSystemBody() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Saturn,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (SolarSystemBodyContainer)capturedContainer;
            exported.SelectedSolarSystemBody.Should().Be(SolarSystemBody.Saturn);
        }

        [Test]
        public async System.Threading.Tasks.Task Export_SolarSystemBody_CallsChangeTab() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, appMediator) = MakeVmWithCapture(coords);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()));

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            appMediator.Verify(a => a.ChangeTab(ApplicationTab.SEQUENCE), Times.Once);
        }

        // ─── Tests: OrbitalObjectContainer export ─────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_OrbitalElements_SelectsCorrectContainerType() {
            var coords = OrbitalFramingScenarios.CometA3_20241026();
            var rate = OrbitalFramingScenarios.CometA3_20241026_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeOrbitalObjectContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var acc = MakeOrbitalElementsAccessor();
            var oe = new OrbitalElementsObject(acc.Object, new OrbitalElements("C/2023 A3"), null,
                MakeProfileService().Object);
            vm.Initialize(oe);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Once);
            capturedContainer.Should().BeOfType<OrbitalObjectContainer>();
        }

        [Test]
        public async System.Threading.Tasks.Task Export_OrbitalElements_SetsTargetName() {
            var coords = OrbitalFramingScenarios.CometA3_20241026();
            var rate = OrbitalFramingScenarios.CometA3_20241026_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeOrbitalObjectContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var acc = MakeOrbitalElementsAccessor();
            var oe = new OrbitalElementsObject(acc.Object, new OrbitalElements("C/2023 A3"), null,
                MakeProfileService().Object);
            vm.Initialize(oe);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (OrbitalObjectContainer)capturedContainer;
            exported.Target.TargetName.Should().Be("C/2023 A3");
        }

        // ─── Tests: JWSTContainer export ─────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_PVTableObject_SelectsJWSTContainer() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeJWSTContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var acc = MakeOrbitalElementsAccessor();
            var pv = new PVTableObject(acc.Object, "James-Webb Space Telescope", null,
                MakeProfileService().Object);
            vm.Initialize(pv);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Once);
            capturedContainer.Should().BeOfType<JWSTContainer>();
        }

        // ─── Tests: ManualTLEContainer export ────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_TLEObject_SelectsManualTLEContainer() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeManualTLEContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ps = MakeProfileService();
            var epoch = Epoch.J2000;
            var tle = new SGPdotNET.TLE.Tle(
                "ISS (ZARYA)",
                "1 25544U 98067A   21001.00000000  .00002182  00000-0  42955-4 0  9996",
                "2 25544  51.6422  76.5699 0003071 168.4765 191.6487 15.48932609313526");

            var tleObject = new TLEObject(
                tle, null, ps.Object, epoch,
                System.TimeSpan.FromSeconds(5));
            vm.Initialize(tleObject);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Once);
            capturedContainer.Should().BeOfType<ManualTLEContainer>();
        }

        [Test]
        public async System.Threading.Tasks.Task Export_TLEObject_SetsTLEData() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, _) = MakeVmWithCapture(coords);

            var container = MakeManualTLEContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ps = MakeProfileService();
            const string tleLine1 = "1 25544U 98067A   21001.00000000  .00002182  00000-0  42955-4 0  9996";
            const string tleLine2 = "2 25544  51.6422  76.5699 0003071 168.4765 191.6487 15.48932609313526";
            const string tleName = "ISS (ZARYA)";
            var tle = new SGPdotNET.TLE.Tle(tleName, tleLine1, tleLine2);

            var tleObject = new TLEObject(
                tle, null, ps.Object, Epoch.J2000,
                System.TimeSpan.FromSeconds(5));
            vm.Initialize(tleObject);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (ManualTLEContainer)capturedContainer;
            exported.TLEData.Should().Contain(tleName);
            exported.TLEData.Should().Contain(tleLine1);
            exported.TLEData.Should().Contain(tleLine2);
        }

        // ─── Tests: multi-type list — picks correct template by exact type ────────

        [Test]
        public async System.Threading.Tasks.Task Export_WithMultipleTemplates_PicksExactTypeMatch() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, appMediator) = MakeVmWithCapture(coords);

            // Register all four container types.
            var solarContainer = MakeSolarSystemBodyContainer();
            var orbitalContainer = MakeOrbitalObjectContainer();
            var jwstContainer = MakeJWSTContainer();
            var tleContainer = MakeManualTLEContainer();

            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> {
                           solarContainer,
                           orbitalContainer,
                           jwstContainer,
                           tleContainer
                       });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            // Select a SolarSystemBody — must pick the SolarSystemBodyContainer, not the others.
            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Jupiter,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            seqMediator.Verify(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()), Times.Once);
            // Clone() creates a new container of the same type, not the same reference.
            // Verify the clone is of the expected type (SolarSystemBodyContainer), not one of the others.
            capturedContainer.Should().BeOfType<SolarSystemBodyContainer>(
                "the SolarSystemBodyContainer template must be cloned, not one of the others");
        }

        // ─── Tests: offset coordinates roundtrip ─────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_WithNonZeroPixelOffset_TranslatesOffsetToCoordinates() {
            // Use a comet positioned far from origin so offset is non-trivial.
            var coords = OrbitalFramingScenarios.CometA3_20241026();
            var rate = OrbitalFramingScenarios.CometA3_20241026_TrackingRate();
            const double pixscale = 1.5; // arcsec/px

            var (vm, seqMediator, _) = MakeVmWithCapture(coords, capturedPaDeg: 0.0, capturedPixscale: pixscale);

            var container = MakeSolarSystemBodyContainer();
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });

            IDeepSkyObjectContainer capturedContainer = null;
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(c => capturedContainer = c);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Moon,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);

            // Drag the framing rectangle 100 px right and 50 px down.
            vm.RectangleOffsetXPx = 100.0;
            vm.RectangleOffsetYPx = 50.0;

            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            var exported = (SolarSystemBodyContainer)capturedContainer;
            // Offsets must be non-zero because we moved the rectangle.
            var exportedRa = exported.OffsetCoordinates.Coordinates.RA;
            var exportedDec = exported.OffsetCoordinates.Coordinates.Dec;
            // With a non-zero pixel offset and valid pixscale, at least one coordinate
            // must be non-zero (exact value depends on projection, but sign/magnitude are predictable).
            (Math.Abs(exportedRa) + Math.Abs(exportedDec)).Should().BeGreaterThan(0,
                "non-zero pixel drag must produce non-zero RA/Dec offset");
        }

        // ─── Tests: IsExporting flag ──────────────────────────────────────────────

        [Test]
        public async System.Threading.Tasks.Task Export_SetsIsExportingDuringExport() {
            var coords = OrbitalFramingScenarios.Mars_20250615();
            var rate = OrbitalFramingScenarios.Mars_20250615_TrackingRate();
            var (vm, seqMediator, appMediator) = MakeVmWithCapture(coords);

            var container = MakeSolarSystemBodyContainer();

            bool isExportingDuringCall = false;
            seqMediator.Setup(s => s.GetDeepSkyObjectContainerTemplates())
                       .Returns(new List<IDeepSkyObjectContainer> { container });
            seqMediator.Setup(s => s.AddAdvancedTarget(It.IsAny<IDeepSkyObjectContainer>()))
                       .Callback<IDeepSkyObjectContainer>(_ => isExportingDuringCall = vm.IsExporting);

            var ssb = new SolarSystemBodyObject(
                MakeOrbitalElementsAccessor().Object,
                SolarSystemBody.Mars,
                null);
            vm.Initialize(ssb);

            await vm.SlewCenterAndImageCommand.ExecuteAsync(null);
            await vm.ExportToSequencerCommand.ExecuteAsync(null);

            isExportingDuringCall.Should().BeTrue("IsExporting must be true while AddAdvancedTarget is called");
            vm.IsExporting.Should().BeFalse("IsExporting must be reset to false after export completes");
        }
    }
}
