#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Astrometry;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    /// <summary>
    /// ViewModel for the Orbital Framing Wizard dialog.
    /// Phase B: capture via ICaptureSource, live coordinate refresh, basic offset tracking.
    /// </summary>
    [Export(typeof(OrbitalFramingWizardVM))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class OrbitalFramingWizardVM : BaseINPC, IDisposable {
        private readonly IProfileService profileService;
        private readonly ICaptureSource captureSource;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly ISequenceMediator sequenceMediator;
        private readonly IApplicationMediator applicationMediator;

        private OrbitalsObjectBase selectedObject;
        private CancellationTokenSource captureCts;
        private DispatcherTimer liveTimer;
        private bool _disposed;

        public OrbitalFramingWizardVM(
            IProfileService profileService,
            IEnumerable<ICaptureSource> captureSources,
            INighttimeCalculator nighttimeCalculator,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions,
            ISequenceMediator sequenceMediator,
            IApplicationMediator applicationMediator) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;
            this.sequenceMediator = sequenceMediator;
            this.applicationMediator = applicationMediator;

            // Exactly one capture source must be registered for Phase B–D.
            var sourceList = captureSources?.ToList()
                ?? throw new ArgumentNullException(nameof(captureSources));
            if (sourceList.Count == 0)
                throw new ArgumentException("No ICaptureSource implementations are registered.", nameof(captureSources));
            if (sourceList.Count > 1)
                throw new ArgumentException($"Expected exactly one ICaptureSource; found {sourceList.Count}. Use CaptureMode selection (Phase E).", nameof(captureSources));
            this.captureSource = sourceList[0];

            SlewCenterAndImageCommand = new AsyncRelayCommand(SlewCenterAndImageAsync, () => !IsCapturing);
            CancelCaptureCommand = new RelayCommand(CancelCapture, () => IsCapturing);

            ResetFramingCommand = new RelayCommand(ResetFraming);

            ExportToSequencerCommand = new AsyncRelayCommand(ExportToSequencerAsync);

            CancelCommand = new RelayCommand(Cancel);
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        /// <summary>
        /// Called by <see cref="OrbitalsVM"/> before the window is shown.
        /// </summary>
        public void Initialize(OrbitalsObjectBase obj) {
            selectedObject = obj;
            Name = obj.Name;
            NighttimeData = nighttimeCalculator.Calculate();

            // Seed live data immediately.
            UpdateLiveData();

            // Start the live refresh timer.
            liveTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromSeconds(2)
            };
            liveTimer.Tick += (_, __) => UpdateLiveData();
            liveTimer.Start();
        }

        /// <summary>Stop the live timer (e.g. when the window closes).</summary>
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            liveTimer?.Stop();
            liveTimer = null;
            captureCts?.Cancel();
            captureCts?.Dispose();
            captureCts = null;
        }

        // -------------------------------------------------------------------------
        // Live update
        // -------------------------------------------------------------------------

        private void UpdateLiveData() {
            if (selectedObject == null) return;

            try {
                var pv = selectedObject.PositionAt(DateTime.UtcNow);
                var coords = pv.Coordinates;
                CurrentRAString = coords.RAString;
                CurrentDecString = coords.DecString;

                var rate = pv.TrackingRate;
                RATrackingRate = rate.RAArcsecsPerSec;
                DecTrackingRate = rate.DecArcsecsPerSec;

                // Rough max exposure: 1 pixel of drift.
                double pixelSize = profileService.ActiveProfile.CameraSettings.PixelSize;
                double focalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength;
                double pixscale = (pixelSize > 0 && focalLength > 0)
                    ? 206.265 * pixelSize / focalLength
                    : 1.0;
                double totalRateArcsecPerSec = Math.Sqrt(
                    rate.RAArcsecsPerSec * rate.RAArcsecsPerSec +
                    rate.DecArcsecsPerSec * rate.DecArcsecsPerSec);
                MaxExposureSeconds = totalRateArcsecPerSec > 0 ? pixscale / totalRateArcsecPerSec : double.NaN;
            } catch (Exception e) {
                Logger.Error("Error updating live data in OrbitalFramingWizardVM", e);
            }
        }

        // -------------------------------------------------------------------------
        // Command implementations
        // -------------------------------------------------------------------------

        private async Task SlewCenterAndImageAsync() {
            using var cts = new CancellationTokenSource();
            captureCts = cts;

            IsCapturing = true;
            HasCapture = false;

            try {
                // Use a null-safe no-op progress for capture; the capture source
                // is responsible for sending its own status messages.
                IProgress<ApplicationStatus> progress = null;

                var frame = await captureSource.CaptureAsync(
                    selectedObject,
                    new OrbitalFramingExposureSettings {
                        ExposureTime = ExposureTime,
                        Gain = Gain,
                        Offset = Offset,
                    },
                    progress,
                    cts.Token);

                CapturedImage = frame.Image;
                CapturedImageRotation = frame.PositionAngleDeg;
                CapturedImagePixscale = frame.PixscaleArcsecPerPx;
                CapturedImageCoordinates = frame.Coordinates;
                CapturedImageWidthPx = frame.WidthPx;
                CapturedImageHeightPx = frame.HeightPx;

                // Phase C will fetch background image; leave null for Phase B.
                BackgroundImage = null;

                // Initialise offsets — rectangle centred on the body.
                _rectangleOffsetXPx = 0;
                _rectangleOffsetYPx = 0;
                _rectangleRotationDeg = 0;
                RaisePropertyChanged(nameof(RectangleOffsetXPx));
                RaisePropertyChanged(nameof(RectangleOffsetYPx));
                RaisePropertyChanged(nameof(RectangleRotationDeg));

                RAOffsetHours = 0;
                DecOffsetDegrees = 0;
                // Compute the "natural" sequencer PA from the captured image rotation.
                // CapturedImageRotation is the camera's position angle on sky; the
                // sequencer uses the complementary convention: PA = (360 - imgPA) % 360.
                FinalPositionAngle = (((360.0 - frame.PositionAngleDeg) % 360.0) + 360.0) % 360.0;
                OffsetSeparationArcsec = 0;
                OffsetPositionAngleDeg = 0;

                HasCapture = true;
            } catch (OperationCanceledException) {
                Logger.Info("Orbital Framing Wizard capture cancelled");
            } catch (FileNotFoundException ex) {
                Logger.Error("Orbital Framing Wizard capture failed: stub frame not found", ex);
                NINA.Core.Utility.Notification.Notification.ShowError($"XISF stub frame not found: {ex.FileName}");
            } catch (Exception e) {
                Logger.Error("Orbital Framing Wizard capture failed", e);
                NINA.Core.Utility.Notification.Notification.ShowError($"Capture failed: {e.Message}");
            } finally {
                IsCapturing = false;
                captureCts = null;
            }
        }

        private void CancelCapture() {
            captureCts?.Cancel();
        }

        private void ResetFraming() {
            // Reset canvas state (pixel offsets and rotation).
            _rectangleOffsetXPx = 0;
            _rectangleOffsetYPx = 0;
            _rectangleRotationDeg = 0;
            RaisePropertyChanged(nameof(RectangleOffsetXPx));
            RaisePropertyChanged(nameof(RectangleOffsetYPx));
            RaisePropertyChanged(nameof(RectangleRotationDeg));

            // Reset the derived offset properties.
            RAOffsetHours = 0;
            DecOffsetDegrees = 0;
            // When a capture exists, restore the natural PA derived from the captured image.
            FinalPositionAngle = HasCapture
                ? (((360.0 - CapturedImageRotation) % 360.0) + 360.0) % 360.0
                : 0.0;
            OffsetSeparationArcsec = 0;
            OffsetPositionAngleDeg = 0;
        }

        /// <summary>
        /// Converts the current canvas pixel offset (from centre) into RA/Dec offsets
        /// and the sky-frame (separation, position-angle) representation.
        /// Offsets are computed RELATIVE TO THE BODY'S CURRENT POSITION so that
        /// <c>OrbitalsContainerBase.RefreshCoordinates()</c> can apply them on every
        /// refresh as the body moves.
        /// </summary>
        private void RecalculateOffsets() {
            if (!HasCapture) return;
            if (CapturedImagePixscale <= 0) return;
            if (CapturedImageCoordinates == null) return;
            if (selectedObject == null) return;

            // Step 1: Apply the pixel offset to the captured image centre coordinates
            // to get the absolute sky position of the framing target.
            // Coordinates.Shift(deltaX, deltaY, rotation, scaleX, scaleY):
            //   - deltaX / deltaY are in pixels (positive X → right on screen, positive Y → down).
            //   - scaleX / scaleY are arcsec/pixel.
            //   - rotation is the image position angle (clockwise degrees, N-up convention).
            // NINA's Shift() already handles the screen-Y-to-sky-Dec inversion internally;
            // do NOT negate _rectangleOffsetYPx here (double negation would invert the Dec axis).
            var framingTarget = CapturedImageCoordinates.Shift(
                _rectangleOffsetXPx,
                _rectangleOffsetYPx,
                CapturedImageRotation,
                CapturedImagePixscale,
                CapturedImagePixscale);

            // Step 2: Get the body's current sky position.
            var bodyCoords = selectedObject.PositionAt(DateTime.UtcNow).Coordinates;

            // Step 3: Offset = framing target MINUS body current position (body-relative).
            double rawRaDiff = framingTarget.RA - bodyCoords.RA;
            // Normalise to [-12, +12] hours.
            while (rawRaDiff > 12.0) rawRaDiff -= 24.0;
            while (rawRaDiff < -12.0) rawRaDiff += 24.0;
            RAOffsetHours = rawRaDiff;
            DecOffsetDegrees = framingTarget.Dec - bodyCoords.Dec;

            // Step 4: Sky-frame (separation, PA) from the body to the framing target.
            // Property setters already raise PropertyChanged — no manual calls needed.
            OffsetSeparationArcsec = OrbitalOffsetMath.AngularSeparation(bodyCoords, framingTarget);
            OffsetPositionAngleDeg = OrbitalOffsetMath.PositionAngleNToE(bodyCoords, framingTarget);
        }

        private async Task ExportToSequencerAsync() {
            if (selectedObject == null || !HasCapture) {
                Notification.ShowWarning("No target selected or no capture available for export.");
                return;
            }

            IsExporting = true;
            try {
                // Step 1: Resolve the matching container type.
                var templates = sequenceMediator.GetDeepSkyObjectContainerTemplates();
                Type containerType = GetContainerTypeForObject(selectedObject);
                var template = templates?.FirstOrDefault(t => t.GetType() == containerType);

                if (template == null) {
                    Notification.ShowError(
                        $"No sequence container template found for {selectedObject.GetType().Name}. " +
                        $"Please add a {containerType.Name} container to the sequencer first.");
                    return;
                }

                // Step 2: Clone the template and cast to the concrete container base.
                var rawClone = template.Clone();

                // Step 3: Populate common fields.
                PopulateContainerSpecificFields(rawClone, selectedObject);

                // Step 4: Set the offset coordinates and position angle on the base class.
                if (rawClone is OrbitalsContainerBase<OrbitalElementsObject> oecBase) {
                    oecBase.Target.PositionAngle = FinalPositionAngle;
                    oecBase.OffsetCoordinates.Coordinates = new Coordinates(
                        Angle.ByHours(RAOffsetHours),
                        Angle.ByDegree(DecOffsetDegrees),
                        Epoch.J2000);
                } else if (rawClone is OrbitalsContainerBase<SolarSystemBodyObject> ssbBase) {
                    ssbBase.Target.PositionAngle = FinalPositionAngle;
                    ssbBase.OffsetCoordinates.Coordinates = new Coordinates(
                        Angle.ByHours(RAOffsetHours),
                        Angle.ByDegree(DecOffsetDegrees),
                        Epoch.J2000);
                } else if (rawClone is OrbitalsContainerBase<TLEObject> tlBase) {
                    tlBase.Target.PositionAngle = FinalPositionAngle;
                    tlBase.OffsetCoordinates.Coordinates = new Coordinates(
                        Angle.ByHours(RAOffsetHours),
                        Angle.ByDegree(DecOffsetDegrees),
                        Epoch.J2000);
                } else if (rawClone is OrbitalsContainerBase<PVTableObject> pvBase) {
                    pvBase.Target.PositionAngle = FinalPositionAngle;
                    pvBase.OffsetCoordinates.Coordinates = new Coordinates(
                        Angle.ByHours(RAOffsetHours),
                        Angle.ByDegree(DecOffsetDegrees),
                        Epoch.J2000);
                }

                // Step 5: Add to sequencer and navigate.
                if (rawClone is IDeepSkyObjectContainer dsoContainer) {
                    sequenceMediator.AddAdvancedTarget(dsoContainer);
                }
                applicationMediator.ChangeTab(ApplicationTab.SEQUENCE);

                // Close the wizard.
                Dispose();
                CloseRequested?.Invoke(this, EventArgs.Empty);

            } finally {
                IsExporting = false;
            }
        }

        private static Type GetContainerTypeForObject(OrbitalsObjectBase obj) {
            return obj switch {
                OrbitalElementsObject => typeof(OrbitalObjectContainer),
                SolarSystemBodyObject => typeof(SolarSystemBodyContainer),
                TLEObject => typeof(ManualTLEContainer),
                PVTableObject => typeof(JWSTContainer),
                _ => throw new InvalidOperationException($"Unknown orbital object type: {obj.GetType().Name}")
            };
        }

        private static void PopulateContainerSpecificFields(object container, OrbitalsObjectBase selectedObject) {
            switch (selectedObject) {
                case OrbitalElementsObject oe when container is OrbitalObjectContainer ooc:
                    // SelectedOrbitalName setter triggers the lookup in IOrbitalElementsAccessor.
                    // ObjectType is inherited from the user's template; the name lookup uses it.
                    ooc.SelectedOrbitalName = oe.OrbitalElements?.Name;
                    ooc.Target.TargetName = selectedObject.Name;
                    break;
                case SolarSystemBodyObject ssb when container is SolarSystemBodyContainer ssbc:
                    ssbc.SelectedSolarSystemBody = ssb.SolarSystemBody;
                    // SelectedSolarSystemBody setter already updates Target.TargetName.
                    break;
                case TLEObject tle when container is ManualTLEContainer tlec:
                    if (tle.Tle != null) {
                        // Reconstruct the 3-line TLE text from the parsed Tle object.
                        string tleText = tle.Tle.Name + Environment.NewLine
                                       + tle.Tle.Line1 + Environment.NewLine
                                       + tle.Tle.Line2;
                        tlec.TLEData = tleText;
                    }
                    break;
                case PVTableObject pv when container is JWSTContainer jwstc:
                    // JWSTContainer pre-loads from the IOrbitalElementsAccessor; just set the name.
                    jwstc.Target.TargetName = selectedObject.Name;
                    break;
            }
        }

        private void Cancel() {
            Dispose();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        // -------------------------------------------------------------------------
        // Event for view to close
        // -------------------------------------------------------------------------

        /// <summary>Raised when the Cancel command is executed.</summary>
        public event EventHandler CloseRequested;

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        private string name;
        public string Name {
            get => name;
            private set { if (name != value) { name = value; RaisePropertyChanged(); } }
        }

        private string currentRAString;
        public string CurrentRAString {
            get => currentRAString;
            private set { if (currentRAString != value) { currentRAString = value; RaisePropertyChanged(); } }
        }

        private string currentDecString;
        public string CurrentDecString {
            get => currentDecString;
            private set { if (currentDecString != value) { currentDecString = value; RaisePropertyChanged(); } }
        }

        private double raTrackingRate;
        public double RATrackingRate {
            get => raTrackingRate;
            private set { if (raTrackingRate != value) { raTrackingRate = value; RaisePropertyChanged(); } }
        }

        private double decTrackingRate;
        public double DecTrackingRate {
            get => decTrackingRate;
            private set { if (decTrackingRate != value) { decTrackingRate = value; RaisePropertyChanged(); } }
        }

        private double maxExposureSeconds = double.NaN;
        public double MaxExposureSeconds {
            get => maxExposureSeconds;
            private set { if (maxExposureSeconds != value) { maxExposureSeconds = value; RaisePropertyChanged(); } }
        }

        private double exposureTime = 30.0;
        public double ExposureTime {
            get => exposureTime;
            set { if (exposureTime != value) { exposureTime = value; RaisePropertyChanged(); } }
        }

        private int gain = 0;
        public int Gain {
            get => gain;
            set { if (gain != value) { gain = value; RaisePropertyChanged(); } }
        }

        private int offset = 0;
        public int Offset {
            get => offset;
            set { if (offset != value) { offset = value; RaisePropertyChanged(); } }
        }

        private BitmapSource capturedImage;
        public BitmapSource CapturedImage {
            get => capturedImage;
            private set { capturedImage = value; RaisePropertyChanged(); }
        }

        private double capturedImageRotation;
        public double CapturedImageRotation {
            get => capturedImageRotation;
            private set { capturedImageRotation = value; RaisePropertyChanged(); }
        }

        private double capturedImagePixscale;
        public double CapturedImagePixscale {
            get => capturedImagePixscale;
            private set { capturedImagePixscale = value; RaisePropertyChanged(); }
        }

        private Coordinates capturedImageCoordinates;
        public Coordinates CapturedImageCoordinates {
            get => capturedImageCoordinates;
            private set { capturedImageCoordinates = value; RaisePropertyChanged(); }
        }

        private int capturedImageWidthPx;
        public int CapturedImageWidthPx {
            get => capturedImageWidthPx;
            private set { capturedImageWidthPx = value; RaisePropertyChanged(); }
        }

        private int capturedImageHeightPx;
        public int CapturedImageHeightPx {
            get => capturedImageHeightPx;
            private set { capturedImageHeightPx = value; RaisePropertyChanged(); }
        }

        // Phase C: background DSS image. Null for Phase B.
        private BitmapSource backgroundImage;
        public BitmapSource BackgroundImage {
            get => backgroundImage;
            private set { backgroundImage = value; RaisePropertyChanged(); }
        }

        private double backgroundFovMultiplier = 3.0;
        public double BackgroundFovMultiplier {
            get => backgroundFovMultiplier;
            set { if (backgroundFovMultiplier != value) { backgroundFovMultiplier = value; RaisePropertyChanged(); } }
        }

        private bool isCapturing;
        public bool IsCapturing {
            get => isCapturing;
            private set {
                if (isCapturing != value) {
                    isCapturing = value;
                    RaisePropertyChanged();
                    SlewCenterAndImageCommand?.NotifyCanExecuteChanged();
                    CancelCaptureCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        private bool hasCapture;
        public bool HasCapture {
            get => hasCapture;
            private set { if (hasCapture != value) { hasCapture = value; RaisePropertyChanged(); } }
        }

        private bool isExporting;
        public bool IsExporting {
            get => isExporting;
            private set { if (isExporting != value) { isExporting = value; RaisePropertyChanged(); } }
        }

        private NighttimeData nighttimeData;
        public NighttimeData NighttimeData {
            get => nighttimeData;
            private set { nighttimeData = value; RaisePropertyChanged(); }
        }

        /// <summary>Phase B: hardcoded label; later phases will update this per mode.</summary>
        public string CaptureButtonLabel => "Load Test Image";

        private double raOffsetHours;
        public double RAOffsetHours {
            get => raOffsetHours;
            private set { if (raOffsetHours != value) { raOffsetHours = value; RaisePropertyChanged(); } }
        }

        private double decOffsetDegrees;
        public double DecOffsetDegrees {
            get => decOffsetDegrees;
            private set { if (decOffsetDegrees != value) { decOffsetDegrees = value; RaisePropertyChanged(); } }
        }

        private double finalPositionAngle;
        public double FinalPositionAngle {
            get => finalPositionAngle;
            private set { if (finalPositionAngle != value) { finalPositionAngle = value; RaisePropertyChanged(); } }
        }

        private double offsetSeparationArcsec;
        public double OffsetSeparationArcsec {
            get => offsetSeparationArcsec;
            private set { if (offsetSeparationArcsec != value) { offsetSeparationArcsec = value; RaisePropertyChanged(); } }
        }

        private double offsetPositionAngleDeg;
        public double OffsetPositionAngleDeg {
            get => offsetPositionAngleDeg;
            private set { if (offsetPositionAngleDeg != value) { offsetPositionAngleDeg = value; RaisePropertyChanged(); } }
        }

        // ─── Canvas state (updated by OrbitalFramingCanvas via TwoWay bindings) ─

        private double _rectangleOffsetXPx = 0;
        /// <summary>Horizontal canvas-pixel offset of the framing rectangle from centre.</summary>
        public double RectangleOffsetXPx {
            get => _rectangleOffsetXPx;
            set {
                if (_rectangleOffsetXPx != value) {
                    _rectangleOffsetXPx = value;
                    RaisePropertyChanged();
                    RecalculateOffsets();
                }
            }
        }

        private double _rectangleOffsetYPx = 0;
        /// <summary>Vertical canvas-pixel offset of the framing rectangle from centre.</summary>
        public double RectangleOffsetYPx {
            get => _rectangleOffsetYPx;
            set {
                if (_rectangleOffsetYPx != value) {
                    _rectangleOffsetYPx = value;
                    RaisePropertyChanged();
                    RecalculateOffsets();
                }
            }
        }

        private double _rectangleRotationDeg = 0;
        /// <summary>Framing-rectangle rotation in degrees (relative to captured image).</summary>
        public double RectangleRotationDeg {
            get => _rectangleRotationDeg;
            set {
                if (_rectangleRotationDeg != value) {
                    _rectangleRotationDeg = value;
                    RaisePropertyChanged();
                    // Final PA = camera-image PA + rectangle rotation, normalised.
                    // FinalPositionAngle setter already raises PropertyChanged — no manual call needed.
                    FinalPositionAngle = (((360.0 - (CapturedImageRotation + _rectangleRotationDeg)) % 360.0) + 360.0) % 360.0;
                }
            }
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        public AsyncRelayCommand SlewCenterAndImageCommand { get; private set; }
        public RelayCommand CancelCaptureCommand { get; private set; }
        public RelayCommand ResetFramingCommand { get; private set; }
        public AsyncRelayCommand ExportToSequencerCommand { get; private set; }
        public RelayCommand CancelCommand { get; private set; }
    }
}
