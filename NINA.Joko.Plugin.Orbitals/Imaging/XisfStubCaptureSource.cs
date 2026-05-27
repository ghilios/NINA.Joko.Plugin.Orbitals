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
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugin.Orbitals.Imaging {

    /// <summary>
    /// Loads a single image from disk (any format NINA supports — FITS, XISF,
    /// TIFF, PNG, RAW, …) and returns it as the wizard's captured frame, with
    /// fake metadata derived from the body's current ephemeris position. Used
    /// for testing the framing workflow without slewing a real mount.
    ///
    /// On first capture in a session the user is shown a file-picker dialog;
    /// the chosen path is then reused for subsequent captures so the same
    /// image is returned over and over until the wizard window is closed.
    /// </summary>
    [Export(typeof(ICaptureSource))]
    [ExportMetadata("Mode", "XisfStub")]
    public class XisfStubCaptureSource : ICaptureSource {
        private readonly IProfileService profileService;
        private readonly IImageDataFactory imageDataFactory;
        private readonly Random rng = new Random();

        // Cached image path — set on first capture by the file-picker dialog,
        // reused on subsequent captures so the user isn't prompted every time.
        // Lives for the lifetime of this capture-source instance (which is per
        // wizard session because the wizard VM is NonShared MEF).
        private string cachedImagePath;
        private readonly object cachedImagePathLock = new object();

        [ImportingConstructor]
        public XisfStubCaptureSource(IProfileService profileService, IImageDataFactory imageDataFactory) {
            this.profileService = profileService;
            this.imageDataFactory = imageDataFactory;
        }

        public async Task<CapturedFrame> CaptureAsync(
            OrbitalsObjectBase target,
            OrbitalFramingExposureSettings exposure,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            // Step 1: resolve a file path. Show the dialog on the UI thread if
            // we haven't picked one yet in this session.
            string path;
            lock (cachedImagePathLock) {
                path = cachedImagePath;
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Pick a test image…" });
                path = await PromptForImagePathAsync(ct);
                if (string.IsNullOrEmpty(path)) {
                    throw new OperationCanceledException("User cancelled the test-image picker.");
                }
                lock (cachedImagePathLock) {
                    cachedImagePath = path;
                }
            }

            if (!BaseImageData.FileIsSupported(path)) {
                throw new FileNotFoundException($"File type not supported by NINA's image loader: {path}", path);
            }

            ct.ThrowIfCancellationRequested();
            progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = $"Loading {Path.GetFileName(path)}…" });

            // Step 2: load via NINA's image-data factory (matches the simulator
            // camera's path — handles FITS / XISF / TIFF / PNG / RAW / etc.) and
            // apply the user's profile auto-stretch settings so the framing
            // canvas displays the image the same way NINA's viewer would.
            BitmapSource bitmap = null;
            int width = 0, height = 0;
            try {
                int bitDepth = (int)(profileService?.ActiveProfile?.CameraSettings?.BitDepth ?? 16);
                var rawConverter = profileService?.ActiveProfile?.CameraSettings?.RawConverter ?? default;
                var imageData = await imageDataFactory.CreateFromFile(
                    path,
                    bitDepth,
                    isBayered: false,
                    rawConverter: rawConverter);

                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Auto-stretching…" });
                var rendered = imageData.RenderImage();

                var imgSettings = profileService?.ActiveProfile?.ImageSettings;
                double factor = imgSettings?.AutoStretchFactor ?? 0.2;
                double blackClipping = imgSettings?.BlackClipping ?? -2.8;
                rendered = await rendered.Stretch(factor, blackClipping, unlinked: false);

                bitmap = rendered.Image;
                if (bitmap != null && bitmap.CanFreeze && !bitmap.IsFrozen) bitmap.Freeze();
                width = bitmap?.PixelWidth ?? 0;
                height = bitmap?.PixelHeight ?? 0;
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning($"XisfStubCaptureSource: NINA pipeline load failed for '{path}' ({ex.Message}); using synthetic fallback.");
                bitmap = null;
            }

            if (bitmap == null) {
                // Synthetic fallback — keep development unblocked when the file
                // can't be loaded for any reason.
                const int fallbackWidth = 640;
                const int fallbackHeight = 480;
                bitmap = CreateSyntheticBitmap(fallbackWidth, fallbackHeight);
                bitmap.Freeze();
                width = fallbackWidth;
                height = fallbackHeight;
            }

            ct.ThrowIfCancellationRequested();

            // Step 3: compute metadata from profile and current orbital position.
            double pixelSize = profileService.ActiveProfile.CameraSettings.PixelSize;       // µm
            double focalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength; // mm
            double pixscale = (pixelSize > 0 && focalLength > 0)
                ? AstroUtil.ArcsecPerPixel(pixelSize, focalLength)
                : 1.0;

            var pv = target.PositionAt(DateTime.UtcNow);
            var coordinates = pv.Coordinates;

            double positionAngle = rng.NextDouble() * 360.0;

            progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = string.Empty });

            return new CapturedFrame {
                Image = bitmap,
                WidthPx = width,
                HeightPx = height,
                Coordinates = coordinates,
                PositionAngleDeg = positionAngle,
                PixscaleArcsecPerPx = pixscale,
            };
        }

        /// <summary>
        /// Prompts the user with an Open File dialog filtered to the same image
        /// extensions NINA's image loader supports. Marshals to the UI thread
        /// because <see cref="CaptureAsync"/> runs on a background task.
        /// </summary>
        private static async Task<string> PromptForImagePathAsync(CancellationToken ct) {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) {
                throw new InvalidOperationException("No WPF dispatcher available to show file picker.");
            }

            return await dispatcher.InvokeAsync(() => {
                ct.ThrowIfCancellationRequested();
                var dlg = new Microsoft.Win32.OpenFileDialog {
                    Title = "Choose a test image for the framing wizard",
                    Filter = "Supported image files|" +
                             "*.fits;*.fit;*.xisf;*.tif;*.tiff;*.png;*.dng;*.jpg;*.jpeg;*.gif;" +
                             "*.cr2;*.cr3;*.nef;*.arw;*.raf;*.raw;*.pef;*.orf" +
                             "|All files|*.*",
                    CheckFileExists = true,
                };
                return dlg.ShowDialog() == true ? dlg.FileName : null;
            }).Task;
        }

        /// <summary>
        /// Creates a synthetic 640×480 32-bit BGRA bitmap with a simple gradient
        /// so the image panel shows something non-trivial in the UI.
        /// </summary>
        private static BitmapSource CreateSyntheticBitmap(int width, int height) {
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen()) {
                var brush = new LinearGradientBrush(
                    Colors.MidnightBlue,
                    Colors.DarkSlateBlue,
                    new Point(0, 0),
                    new Point(1, 1));
                ctx.DrawRectangle(brush, null, new Rect(0, 0, width, height));

                var starBrush = Brushes.White;
                var rand = new Random(42);
                for (int i = 0; i < 200; i++) {
                    double x = rand.NextDouble() * width;
                    double y = rand.NextDouble() * height;
                    double r = rand.NextDouble() * 1.5 + 0.5;
                    ctx.DrawEllipse(starBrush, null, new Point(x, y), r, r);
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
