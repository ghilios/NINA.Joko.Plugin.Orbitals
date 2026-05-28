#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugin.Orbitals.Imaging {

    /// <summary>
    /// Live capture source that slews the mount to the orbital target, plate-solves to centre,
    /// captures the final framing image, and returns it with full astrometric metadata.
    /// </summary>
    [Export(typeof(ICaptureSource))]
    [ExportMetadata("Mode", "Live")]
    public class LiveCaptureSource : ICaptureSource {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;

        [ImportingConstructor]
        public LiveCaptureSource(
            IProfileService profileService,
            ICameraMediator cameraMediator,
            IImagingMediator imagingMediator,
            ITelescopeMediator telescopeMediator,
            IFilterWheelMediator filterWheelMediator,
            IDomeMediator domeMediator,
            IDomeFollower domeFollower,
            IPlateSolverFactory plateSolverFactory) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.imagingMediator = imagingMediator;
            this.telescopeMediator = telescopeMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.plateSolverFactory = plateSolverFactory;
        }

        public async Task<CapturedFrame> CaptureAsync(
            OrbitalsObjectBase target,
            OrbitalFramingExposureSettings exposure,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct) {

            // Step 1: Pre-flight checks.
            var cameraInfo = cameraMediator.GetInfo();
            if (!cameraInfo.Connected) {
                Notification.ShowError("Camera is not connected. Connect the camera before using Live capture.");
                throw new CaptureSourceUserFacingException("Camera not connected");
            }

            var telescopeInfo = telescopeMediator.GetInfo();
            if (!telescopeInfo.Connected) {
                Notification.ShowError("Telescope/mount is not connected. Connect the mount before using Live capture.");
                throw new CaptureSourceUserFacingException("Telescope not connected");
            }

            // Step 2: Get current target coordinates.
            var targetCoords = target.PositionAt(DateTime.UtcNow).Coordinates;

            // Step 3: Build solvers and run the centering loop.
            progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Slewing to target..." });

            var plateSolver = plateSolverFactory.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings);
            var blindSolver = plateSolverFactory.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);
            var centeringSolver = plateSolverFactory.GetCenteringSolver(
                plateSolver, blindSolver,
                imagingMediator, telescopeMediator,
                filterWheelMediator, domeMediator, domeFollower);

            var plateSolveSettings = profileService.ActiveProfile.PlateSolveSettings;
            var telescopeSettings = profileService.ActiveProfile.TelescopeSettings;
            var cameraSettings = profileService.ActiveProfile.CameraSettings;

            var centerCaptureSeq = new CaptureSequence(
                plateSolveSettings.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                plateSolveSettings.Filter,
                new BinningMode(plateSolveSettings.Binning, plateSolveSettings.Binning),
                1);
            centerCaptureSeq.Gain = plateSolveSettings.Gain;

            var centerSolveParams = new CenterSolveParameter {
                Coordinates = targetCoords,
                FocalLength = telescopeSettings.FocalLength,
                PixelSize = cameraSettings.PixelSize,
                Binning = plateSolveSettings.Binning,
                SearchRadius = plateSolveSettings.SearchRadius,
                MaxObjects = plateSolveSettings.MaxObjects,
                Threshold = plateSolveSettings.Threshold,
                Attempts = plateSolveSettings.NumberOfAttempts,
                ReattemptDelay = TimeSpan.FromMinutes(plateSolveSettings.ReattemptDelay),
                Regions = plateSolveSettings.Regions,
                DownSampleFactor = plateSolveSettings.DownSampleFactor,
                NoSync = telescopeSettings.NoSync,
                BlindFailoverEnabled = plateSolveSettings.BlindFailoverEnabled,
            };

            var plateSolveResult = await centeringSolver.Center(centerCaptureSeq, centerSolveParams, null, progress, ct);

            if (plateSolveResult == null || !plateSolveResult.Success) {
                throw new InvalidOperationException("Plate solve failed during centering. Could not capture framing image.");
            }

            // Step 4: Take a fresh framing snapshot using the wizard's exposure
            // settings. We don't reuse the centering loop's last image — that was
            // taken with plate-solve settings (short exposure, plate-solve gain),
            // which usually isn't what the user wants to look at.
            progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Capturing framing image..." });
            var framingSeq = new CaptureSequence(
                exposure.ExposureTime,
                CaptureSequence.ImageTypes.SNAPSHOT,
                null,
                new BinningMode((short)exposure.Binning, (short)exposure.Binning),
                1);
            // -1 in either field tells the camera driver to use the
            // camera-settings default; otherwise the user's typed value wins.
            framingSeq.Gain = exposure.Gain;
            framingSeq.Offset = exposure.Offset;
            var captureResult = await imagingMediator.CaptureAndPrepareImage(
                framingSeq,
                new PrepareImageParameters(true, true),
                ct,
                progress);
            var bitmap = captureResult?.Image;
            if (bitmap == null) {
                throw new InvalidOperationException("Failed to capture framing image.");
            }
            if (!bitmap.IsFrozen) bitmap.Freeze();

            // Step 5: Derive pixel scale.
            double pixelSize = cameraInfo.PixelSize;
            if (pixelSize <= 0) pixelSize = profileService.ActiveProfile.CameraSettings.PixelSize;
            double focalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength;
            double pixscale = (pixelSize > 0 && focalLength > 0)
                ? AstroUtil.ArcsecPerPixel(pixelSize, focalLength)
                : plateSolveResult.Pixscale;
            if (pixscale <= 0) pixscale = 1.0;

            progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = string.Empty });

            // Step 6: Return the captured frame.
            return new CapturedFrame {
                Image = bitmap,
                WidthPx = bitmap.PixelWidth,
                HeightPx = bitmap.PixelHeight,
                Coordinates = plateSolveResult.Coordinates,
                PositionAngleDeg = plateSolveResult.PositionAngle,
                PixscaleArcsecPerPx = pixscale,
            };
        }
    }
}
