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
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.SequenceItems;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.SkySurvey;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private readonly IEnumerable<Lazy<ICaptureSource, ICaptureSourceMetadata>> captureSources;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly ISequenceMediator sequenceMediator;
        private readonly IApplicationMediator applicationMediator;
        private readonly ISkySurveyFactory skySurveyFactory;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly ICameraMediator cameraMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly SkyMapAnnotator skyMapAnnotator;

        // Image dimensions requested from sky-survey providers. NINA's framing assistant
        // uses similar values; large enough to look good at the background FOV scale.
        private const int BackgroundImagePx = 1200;

        private OrbitalsObjectBase selectedObject;
        private CancellationTokenSource captureCts;
        private DispatcherTimer liveTimer;
        private bool _disposed;

        // ═══════════════════════════════════════════════════════════════════════
        //  ── CAPTURE-SOURCE OVERRIDE  (developer-only — set in code, not in UI) ──
        //
        //  Selects which ICaptureSource implementation the wizard uses for the
        //  "Capture" button. There is intentionally NO user-facing setting for
        //  this; flip the value below when you need to switch.
        //
        //    CaptureModeEnum.Live      → real workflow: slew → plate-solve →
        //                                 center → capture using the connected
        //                                 mount + camera.
        //    CaptureModeEnum.XisfStub  → development workflow: prompt for an
        //                                 image file the first time the user
        //                                 clicks Capture, then re-use it for
        //                                 subsequent captures. No mount needed.
        //
        //  This must stay a `const`: it's read into `CaptureButtonLabel` (which
        //  bindings can't refresh) and ResolveCaptureSource (which is on the
        //  hot path for every Capture click). Changing the value requires a
        //  rebuild — that's deliberate so it can't be flipped accidentally in
        //  a release build.
        // ═══════════════════════════════════════════════════════════════════════
        private const CaptureModeEnum CaptureModeOverride = CaptureModeEnum.Live;

        public OrbitalFramingWizardVM(
            IProfileService profileService,
            IEnumerable<Lazy<ICaptureSource, ICaptureSourceMetadata>> captureSources,
            INighttimeCalculator nighttimeCalculator,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions,
            ISequenceMediator sequenceMediator,
            IApplicationMediator applicationMediator,
            ISkySurveyFactory skySurveyFactory,
            ITelescopeMediator telescopeMediator,
            ICameraMediator cameraMediator,
            IGuiderMediator guiderMediator) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;
            this.sequenceMediator = sequenceMediator;
            this.applicationMediator = applicationMediator;
            this.skySurveyFactory = skySurveyFactory;
            this.telescopeMediator = telescopeMediator;
            this.cameraMediator = cameraMediator;
            this.guiderMediator = guiderMediator;

            // Snapshot the camera info so the view can bind to DefaultGain /
            // DefaultOffset for the empty-state hint text in the Capture section.
            // Refreshed on each Initialize() call below.
            CameraInfo = cameraMediator?.GetInfo();

            // Sky-map annotator produces the RA/Dec grid + DSO labels overlay
            // that we layer on top of the raw survey background, matching NINA's
            // framing assistant. PropertyChanged surfaces SkyMapOverlay updates
            // (the annotator re-renders the bitmap whenever the FoV changes or
            // the telescope position updates).
            skyMapAnnotator = new SkyMapAnnotator(telescopeMediator, profileService);
            skyMapAnnotator.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(SkyMapAnnotator.SkyMapOverlay)) {
                    RaisePropertyChanged(nameof(SkyMapOverlay));
                }
            };

            // Seed image source from the profile (mirroring NINA's framing assistant)
            // and fall back to the offline sky atlas if the profile isn't available
            // (which only happens in some unit-test paths).
            selectedImageSource = LoadInitialImageSource();

            this.captureSources = captureSources?.ToList()
                ?? throw new ArgumentNullException(nameof(captureSources));

            SlewCenterAndImageCommand = new AsyncRelayCommand(SlewCenterAndImageAsync, () => !IsCapturing);
            CancelCaptureCommand = new RelayCommand(CancelCapture, () => IsCapturing);

            ResetFramingCommand = new RelayCommand(ResetFraming);

            ExportToSequencerCommand = new AsyncRelayCommand(ExportToSequencerAsync);

            CancelCommand = new RelayCommand(Cancel);
        }

        // ─── Canvas zoom ─────────────────────────────────────────────────────────

        public const double ZoomStep = 1.25;
        public const double MinZoom = 1.0;
        public const double MaxZoom = 8.0;

        private double canvasZoom = 1.0;

        /// <summary>
        /// Visual scale of the framing canvas. 1.0 = fit-to-viewport; values outside
        /// [<see cref="MinZoom"/>, <see cref="MaxZoom"/>] are clamped on assignment.
        /// The wizard view applies this as a <c>LayoutTransform</c> on the canvas
        /// UserControl so the wrapping <c>ScrollViewer</c> shows scrollbars when
        /// content exceeds the viewport. The view code-behind drives this property
        /// from its zoom buttons / Ctrl+wheel and re-centers the scroll offsets after
        /// each change.
        /// </summary>
        public double CanvasZoom {
            get => canvasZoom;
            set {
                if (double.IsNaN(value) || double.IsInfinity(value)) return;
                var clamped = Math.Max(MinZoom, Math.Min(MaxZoom, value));
                if (canvasZoom != clamped) { canvasZoom = clamped; RaisePropertyChanged(); }
            }
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

            // Refresh camera info — the camera may have connected (or been
            // reconfigured) between wizard construction and the window opening.
            CameraInfo = cameraMediator?.GetInfo();

            // Seed live data immediately.
            UpdateLiveData();

            // Start the live refresh timer.
            liveTimer = new DispatcherTimer(DispatcherPriority.Background) {
                Interval = TimeSpan.FromSeconds(2)
            };
            liveTimer.Tick += (_, __) => UpdateLiveData();
            liveTimer.Start();

            // Kick off an initial sky-survey background fetch centered on the body's
            // current J2000 position. Fire-and-forget — the helper handles its own
            // cancellation, error notification, and IsBackgroundLoading flag.
            _ = ReloadBackgroundAsync();
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
            backgroundLoadCts?.Cancel();
            backgroundLoadCts?.Dispose();
            backgroundLoadCts = null;
            GC.SuppressFinalize(this);
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
                    ? AstroUtil.ArcsecPerPixel(pixelSize, focalLength)
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
        // Sky-survey background
        // -------------------------------------------------------------------------

        private CancellationTokenSource backgroundLoadCts;
        private bool isBackgroundLoading;

        /// <summary>True while a sky-survey image is being fetched.</summary>
        public bool IsBackgroundLoading {
            get => isBackgroundLoading;
            private set { if (isBackgroundLoading != value) { isBackgroundLoading = value; RaisePropertyChanged(); } }
        }

        private SkySurveySource selectedImageSource;

        /// <summary>
        /// Which sky-survey source to fetch the background from. Initialized from
        /// <c>ActiveProfile.FramingAssistantSettings.LastSelectedImageSource</c> so
        /// the wizard shares the user's preference with NINA's built-in framing
        /// assistant. Changing this persists the new value back to the same profile
        /// setting and kicks off a re-fetch.
        /// </summary>
        public SkySurveySource SelectedImageSource {
            get => selectedImageSource;
            set {
                if (selectedImageSource == value) return;
                selectedImageSource = value;
                RaisePropertyChanged();
                PersistImageSourceToProfile(value);
                _ = ReloadBackgroundAsync();
            }
        }

        /// <summary>All <see cref="SkySurveySource"/> values for the ComboBox ItemsSource.</summary>
        public IReadOnlyList<SkySurveySource> AvailableImageSources { get; } =
            (SkySurveySource[])Enum.GetValues(typeof(SkySurveySource));

        private SkySurveySource LoadInitialImageSource() {
            // Prefer the user's NINA framing-assistant choice so the wizard shares
            // their configured/working source. If unset or invalid, fall back to
            // HIPS2FITS which is the most reliably-available online source.
            try {
                var settings = profileService?.ActiveProfile?.FramingAssistantSettings;
                var fromProfile = settings?.LastSelectedImageSource ?? SkySurveySource.HIPS2FITS;
                if (!Enum.IsDefined(typeof(SkySurveySource), fromProfile)) {
                    fromProfile = SkySurveySource.HIPS2FITS;
                }
                return fromProfile;
            } catch {
                return SkySurveySource.HIPS2FITS;
            }
        }

        private void PersistImageSourceToProfile(SkySurveySource value) {
            try {
                var settings = profileService?.ActiveProfile?.FramingAssistantSettings;
                if (settings != null) settings.LastSelectedImageSource = value;
            } catch (Exception ex) {
                Logger.Warning($"Could not persist image source to profile: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetches a sky-survey image centered on the current target (post-capture:
        /// the captured frame's plate-solved coordinates; pre-capture: the body's
        /// J2000 position right now) and assigns it to <see cref="BackgroundImage"/>.
        /// Safe to call concurrently — prior in-flight loads are cancelled.
        /// </summary>
        private async Task ReloadBackgroundAsync() {
            if (skySurveyFactory == null) return;
            if (selectedObject == null) return;

            // Cancel any in-flight load so a rapid source-change or coord-change
            // doesn't paint a stale image last.
            backgroundLoadCts?.Cancel();
            backgroundLoadCts?.Dispose();
            backgroundLoadCts = new CancellationTokenSource();
            var ct = backgroundLoadCts.Token;

            Coordinates coords;
            double fovArcmin;
            string name = selectedObject.Name ?? "Target";

            if (HasCapture && CapturedImageCoordinates != null && CapturedImagePixscale > 0 && CapturedImageWidthPx > 0) {
                coords = CapturedImageCoordinates;
                fovArcmin = CapturedImagePixscale * CapturedImageWidthPx * BackgroundFovMultiplier / 60.0;
            } else {
                try {
                    coords = selectedObject.PositionAt(DateTime.UtcNow).Coordinates;
                } catch (Exception ex) {
                    Logger.Error("Could not compute body position for background fetch", ex);
                    return;
                }
                fovArcmin = EstimatePreCaptureFovArcmin();
            }

            // Sky-survey providers expect J2000 coordinates. Transform regardless of
            // what epoch the body / plate-solve reported so the survey image lines up
            // with the body's position on the sky.
            try {
                coords = coords.Transform(Epoch.J2000);
            } catch (Exception ex) {
                Logger.Warning($"Could not transform coords to J2000 ({ex.Message}); using as-is.");
            }

            try {
                IsBackgroundLoading = true;
                var bmp = await FetchSurveyBitmapAsync(SelectedImageSource, name, coords, fovArcmin, ct);

                // Auto-fallback: if the chosen source produced no image (typical when
                // the SKYATLAS local file isn't present, or an online source times out
                // without throwing), try HIPS2FITS once so the user sees *something*
                // and gets a log line they can paste into a bug report.
                if (bmp == null && !ct.IsCancellationRequested && SelectedImageSource != SkySurveySource.HIPS2FITS) {
                    Logger.Info($"Orbital wizard background: {SelectedImageSource} returned no image; falling back to HIPS2FITS");
                    bmp = await FetchSurveyBitmapAsync(SkySurveySource.HIPS2FITS, name, coords, fovArcmin, ct);
                }

                if (ct.IsCancellationRequested) return;

                if (bmp != null && bmp.CanFreeze && !bmp.IsFrozen) bmp.Freeze();
                BackgroundImage = bmp;
                Logger.Info($"Orbital wizard background assigned: bitmap={(bmp == null ? "null" : $"{bmp.PixelWidth}x{bmp.PixelHeight}")}");

                // Initialize the annotator with the SAME center coords / FOV / dims
                // we just fetched the survey at, so its overlay (RA/Dec grid + DSO
                // labels) aligns with the survey image pixel-for-pixel.
                if (bmp != null) {
                    try {
                        await skyMapAnnotator.Initialize(
                            coords,
                            fovArcmin / 60.0,
                            BackgroundImagePx,
                            BackgroundImagePx,
                            0.0,
                            null,
                            ct);
                    } catch (OperationCanceledException) {
                        // expected if the user switched source mid-init
                    } catch (Exception ex) {
                        Logger.Warning($"Sky-map annotator initialize failed: {ex.Message}");
                    }
                }
            } catch (OperationCanceledException) {
                // expected when the user changes source rapidly
            } catch (Exception ex) {
                Logger.Error("Failed to load sky-survey background", ex);
                Notification.ShowError($"Could not load survey image: {ex.Message}");
                BackgroundImage = null;
            } finally {
                IsBackgroundLoading = false;
            }
        }

        private async Task<BitmapSource> FetchSurveyBitmapAsync(
            SkySurveySource source,
            string name,
            Coordinates coords,
            double fovArcmin,
            CancellationToken ct) {
            Logger.Info($"Orbital wizard background fetch: source={source} name='{name}' ra={coords.RA:F6}h dec={coords.Dec:F6}° fov={fovArcmin:F2}arcmin px={BackgroundImagePx}");
            try {
                var survey = skySurveyFactory.Create(source);
                var img = await survey.GetImage(name, coords, fovArcmin, BackgroundImagePx, BackgroundImagePx, ct, null);
                if (ct.IsCancellationRequested) return null;
                if (img == null) {
                    Logger.Info($"Orbital wizard background fetch: source={source} returned a null SkySurveyImage");
                    return null;
                }
                Logger.Info($"Orbital wizard background fetch: source={source} returned image={(img.Image == null ? "null" : $"{img.Image.PixelWidth}x{img.Image.PixelHeight}")}");
                return img.Image;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning($"Orbital wizard background fetch from {source} failed: {ex.Message}");
                return null;
            }
        }

        private double EstimatePreCaptureFovArcmin() {
            try {
                var profile = profileService?.ActiveProfile;
                double pixelSize = profile?.CameraSettings?.PixelSize ?? 0;
                double focalLength = profile?.TelescopeSettings?.FocalLength ?? 0;
                if (pixelSize > 0 && focalLength > 0) {
                    double arcsecPerPx = AstroUtil.ArcsecPerPixel(pixelSize, focalLength);
                    // Assume a 4000 px sensor width as a reasonable default; the
                    // resulting FOV is the wizard's pre-capture estimate only.
                    const int AssumedSensorPx = 4000;
                    double fovArcsec = arcsecPerPx * AssumedSensorPx;
                    return fovArcsec / 60.0 * BackgroundFovMultiplier;
                }
            } catch { }
            // Fallback: 60 arcmin × multiplier.
            return 60.0 * BackgroundFovMultiplier;
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

                var captureSource = ResolveCaptureSource();
                if (captureSource == null) {
                    throw new InvalidOperationException("No ICaptureSource is registered for the current capture mode.");
                }

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

                // Re-fetch the sky-survey background centered on the plate-solved
                // coordinates at the captured FOV (with BackgroundFovMultiplier).
                _ = ReloadBackgroundAsync();
            } catch (CaptureSourceUserFacingException ex) {
                // Capture source already showed the user an error notification — just log.
                Logger.Info($"Capture aborted (user already notified): {ex.Message}");
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
        /// Converts the current captured-image-pixel offset (from centre) into RA/Dec
        /// offsets and the sky-frame (separation, position-angle) representation.
        /// Offsets are computed RELATIVE TO THE BODY'S CURRENT POSITION so that
        /// <c>OrbitalsContainerBase.RefreshCoordinates()</c> can apply them on every
        /// refresh as the body moves.
        /// </summary>
        private void RecalculateOffsets() {
            if (!HasCapture) return;
            if (CapturedImagePixscale <= 0) return;
            if (CapturedImageCoordinates == null) return;
            if (selectedObject == null) return;

            // Step 1: Apply the captured-image-pixel offset to the captured image
            // centre coordinates to get the absolute sky position of the framing target.
            // Coordinates.Shift(deltaX, deltaY, rotation, scaleX, scaleY):
            //   - deltaX / deltaY are in captured-image pixels (sensor frame, positive X → +sensor X, positive Y → +sensor Y).
            //   - scaleX / scaleY are arcsec per captured-image pixel.
            //   - rotation is the captured image's plate-solved PA (clockwise degrees, N-up convention).
            // The canvas converts mouse-drag canvas-pixel deltas to image-pixel deltas
            // before writing them into RectangleOffsetX/YPx, so the units line up here.
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

                // Step 2: Materialize a fresh container instance. Prefer a user
                // template clone if one is registered (preserves their custom
                // sub-items / triggers); otherwise build the default container
                // directly so the export doesn't break for users who haven't
                // saved a personal template for this orbital type.
                ISequenceContainer rawClone;
                if (template != null) {
                    rawClone = (ISequenceContainer)template.Clone();
                } else {
                    try {
                        rawClone = ConstructDefaultContainer(selectedObject);
                    } catch (Exception ex) {
                        Logger.Error($"ExportToSequencerAsync: failed to construct default {containerType.Name}", ex);
                        Notification.ShowError(
                            $"Could not create a {containerType.Name} for {selectedObject.GetType().Name}: {ex.Message}");
                        return;
                    }
                }

                // Step 3: Populate common fields.
                PopulateContainerSpecificFields(rawClone, selectedObject);

                // Step 4: Set the offset coordinates and position angle on the base class.
                // Separation + Offset PA is the canonical, position-independent
                // representation that the container will use to drive its slew math.
                if (rawClone is IOrbitalsOffsetContainer offsetContainer) {
                    offsetContainer.OffsetSeparationArcsec = OffsetSeparationArcsec;
                    offsetContainer.OffsetPositionAngleDeg = OffsetPositionAngleDeg;
                }
                if (rawClone is IDeepSkyObjectContainer dsoForPA) {
                    dsoForPA.Target.PositionAngle = FinalPositionAngle;
                }

                // Step 5: Add to sequencer and navigate.
                if (rawClone is not IDeepSkyObjectContainer dsoContainer) {
                    Notification.ShowError($"Internal error: Clone() returned a non-sequencer-compatible type: {rawClone?.GetType().Name}. Export aborted.");
                    return;
                }

                try {
                    sequenceMediator.AddAdvancedTarget(dsoContainer);
                    applicationMediator.ChangeTab(ApplicationTab.SEQUENCE);
                    Dispose();
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                } catch (Exception innerEx) {
                    Logger.Error("ExportToSequencerAsync: failed to add target to sequencer", innerEx);
                    Notification.ShowError($"Export failed: {innerEx.Message}");
                }

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

        /// <summary>
        /// Builds a fresh container instance using the same dependencies MEF
        /// would inject into the corresponding <c>[ImportingConstructor]</c>.
        /// Used as a fallback when the user has no personalized template saved
        /// for this orbital object type — so the export "just works" on a
        /// clean NINA install.
        /// </summary>
        private ISequenceContainer ConstructDefaultContainer(OrbitalsObjectBase obj) {
            return obj switch {
                OrbitalElementsObject =>
                    new OrbitalObjectContainer(profileService, nighttimeCalculator, applicationMediator),
                SolarSystemBodyObject =>
                    new SolarSystemBodyContainer(profileService, nighttimeCalculator, applicationMediator),
                PVTableObject =>
                    new JWSTContainer(profileService, nighttimeCalculator, applicationMediator),
                TLEObject =>
                    new ManualTLEContainer(profileService, nighttimeCalculator, telescopeMediator, applicationMediator, guiderMediator),
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
                default:
                    Logger.Warning($"PopulateContainerSpecificFields: unhandled combination {selectedObject.GetType().Name} → {container.GetType().Name}. Container-specific fields may be empty.");
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

        // -1 is NINA's "use the camera-settings default" sentinel (same convention
        // as SnapShotControlSettings). The XAML binds these through
        // MinusOneToEmptyStringConverter so the input renders blank.
        private int gain = -1;
        public int Gain {
            get => gain;
            set { if (gain != value) { gain = value; RaisePropertyChanged(); } }
        }

        private int offset = -1;
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

        /// <summary>
        /// Live camera info used to drive default-value hints (e.g. the
        /// <c>"(0)"</c> placeholder shown in the Gain / Offset inputs when the
        /// user hasn't typed a value, matching NINA's snapshot dock).
        /// </summary>
        private CameraInfo cameraInfoSnapshot;
        public CameraInfo CameraInfo {
            get => cameraInfoSnapshot;
            private set { cameraInfoSnapshot = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// Annotation overlay produced by NINA's <see cref="SkyMapAnnotator"/> —
        /// RA/Dec grid lines plus labels for named sky objects. The annotator
        /// re-renders the bitmap whenever its viewport changes, and we surface
        /// its <c>SkyMapOverlay</c> property through here so the canvas can
        /// rebind on every refresh.
        /// </summary>
        public BitmapSource SkyMapOverlay => skyMapAnnotator?.SkyMapOverlay;

        private double backgroundFovMultiplier = 2.0;
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

        /// <summary>Button label tracks the developer-only <see cref="CaptureModeOverride"/>.</summary>
        public string CaptureButtonLabel => CaptureModeOverride == CaptureModeEnum.Live
            ? "Slew, Center & Image"
            : "Load Test Image";

        /// <summary>
        /// Resolves the active <see cref="ICaptureSource"/> from the developer
        /// override <see cref="CaptureModeOverride"/> by matching each source's
        /// MEF metadata. Falls back to the first registered source if no match.
        /// </summary>
        private ICaptureSource ResolveCaptureSource() {
            var modeKey = CaptureModeOverride == CaptureModeEnum.Live ? "Live" : "XisfStub";
            var match = captureSources.FirstOrDefault(s => s.Metadata.Mode == modeKey);
            if (match == null) {
                Logger.Warning($"No ICaptureSource registered for mode '{modeKey}', falling back to first available");
                match = captureSources.FirstOrDefault();
            }
            return match?.Value;
        }

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
            private set {
                if (offsetSeparationArcsec != value) {
                    offsetSeparationArcsec = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(OffsetSeparationDisplay));
                }
            }
        }

        /// <summary>
        /// Separation formatted as d°m′s″ so the user doesn't have to convert
        /// arcseconds mentally for offsets larger than ~60″.
        /// </summary>
        public string OffsetSeparationDisplay {
            get {
                var totalArcsec = Math.Abs(offsetSeparationArcsec);
                var deg = (int)(totalArcsec / 3600.0);
                var arcmin = (int)((totalArcsec - deg * 3600.0) / 60.0);
                var arcsec = totalArcsec - deg * 3600.0 - arcmin * 60.0;
                return $"{deg:D2}° {arcmin:D2}′ {arcsec:F1}″";
            }
        }

        private double offsetPositionAngleDeg;
        public double OffsetPositionAngleDeg {
            get => offsetPositionAngleDeg;
            private set { if (offsetPositionAngleDeg != value) { offsetPositionAngleDeg = value; RaisePropertyChanged(); } }
        }

        // ─── Canvas state (updated by OrbitalFramingCanvas via TwoWay bindings) ─

        private double _rectangleOffsetXPx = 0;
        /// <summary>
        /// Horizontal offset of the framing rectangle from centre, in captured-image
        /// pixels (sensor frame). The canvas converts canvas-pixel drag deltas to
        /// this unit before writing.
        /// </summary>
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
        /// <summary>
        /// Vertical offset of the framing rectangle from centre, in captured-image
        /// pixels (sensor frame). The canvas converts canvas-pixel drag deltas to
        /// this unit before writing.
        /// </summary>
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
        /// <summary>
        /// Framing-rectangle rotation in degrees, interpreted as a delta relative to
        /// the plate-solved <see cref="CapturedImageRotation"/>. A value of 0 means
        /// "frame as captured" (no rotation offset from the camera's actual PA).
        /// </summary>
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
