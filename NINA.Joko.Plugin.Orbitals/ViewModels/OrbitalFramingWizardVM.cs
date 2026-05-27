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
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
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
    public class OrbitalFramingWizardVM : BaseINPC {
        private readonly IProfileService profileService;
        private readonly ICaptureSource captureSource;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;

        private OrbitalsObjectBase selectedObject;
        private CancellationTokenSource captureCts;
        private DispatcherTimer liveTimer;

        public OrbitalFramingWizardVM(
            IProfileService profileService,
            IEnumerable<ICaptureSource> captureSources,
            INighttimeCalculator nighttimeCalculator,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;

            // Exactly one capture source must be registered for Phase B–D.
            this.captureSource = captureSources?.Single()
                ?? throw new ArgumentException("No ICaptureSource implementations are available.", nameof(captureSources));

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
            var cts = new CancellationTokenSource();
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
                RAOffsetHours = 0;
                DecOffsetDegrees = 0;
                FinalPositionAngle = 0;
                OffsetSeparationArcsec = 0;
                OffsetPositionAngleDeg = 0;

                HasCapture = true;
            } catch (OperationCanceledException) {
                Logger.Info("Orbital Framing Wizard capture cancelled");
            } catch (Exception e) {
                Logger.Error("Orbital Framing Wizard capture failed", e);
            } finally {
                IsCapturing = false;
                captureCts = null;
            }
        }

        private void CancelCapture() {
            captureCts?.Cancel();
        }

        private void ResetFraming() {
            RAOffsetHours = 0;
            DecOffsetDegrees = 0;
            FinalPositionAngle = 0;
            OffsetSeparationArcsec = 0;
            OffsetPositionAngleDeg = 0;
        }

        private Task ExportToSequencerAsync() {
            // Phase D implementation.
            throw new NotImplementedException("Export to sequencer is not yet implemented (Phase D).");
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
            set { if (raOffsetHours != value) { raOffsetHours = value; RaisePropertyChanged(); } }
        }

        private double decOffsetDegrees;
        public double DecOffsetDegrees {
            get => decOffsetDegrees;
            set { if (decOffsetDegrees != value) { decOffsetDegrees = value; RaisePropertyChanged(); } }
        }

        private double finalPositionAngle;
        public double FinalPositionAngle {
            get => finalPositionAngle;
            set { if (finalPositionAngle != value) { finalPositionAngle = value; RaisePropertyChanged(); } }
        }

        private double offsetSeparationArcsec;
        public double OffsetSeparationArcsec {
            get => offsetSeparationArcsec;
            set { if (offsetSeparationArcsec != value) { offsetSeparationArcsec = value; RaisePropertyChanged(); } }
        }

        private double offsetPositionAngleDeg;
        public double OffsetPositionAngleDeg {
            get => offsetPositionAngleDeg;
            set { if (offsetPositionAngleDeg != value) { offsetPositionAngleDeg = value; RaisePropertyChanged(); } }
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
