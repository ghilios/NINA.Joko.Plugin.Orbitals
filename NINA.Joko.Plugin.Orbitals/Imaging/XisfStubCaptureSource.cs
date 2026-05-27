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
    /// A stub capture source for Phase B testing.
    /// If <see cref="StubXisfPath"/> exists on disk it attempts to load the file
    /// via WPF's BitmapDecoder; otherwise it falls back to a synthetic gradient
    /// bitmap so development can continue without the real file present.
    /// A missing file always produces a visible error notification.
    /// </summary>
    [Export(typeof(ICaptureSource))]
    [ExportMetadata("Mode", "XisfStub")]
    public class XisfStubCaptureSource : ICaptureSource {
        // Path to the real XISF stub frame used for Phase B UI testing.
        private const string StubXisfPath = @"E:\AP\processing\1_selected\LIGHT_2024-10-26_22-16-36_L_-10.00_120.00s_0069_c_cc_a.xisf";

        private readonly IProfileService profileService;
        private readonly Random rng = new Random();

        [ImportingConstructor]
        public XisfStubCaptureSource(IProfileService profileService) {
            this.profileService = profileService;
        }

        public Task<CapturedFrame> CaptureAsync(
            OrbitalsObjectBase target,
            OrbitalFramingExposureSettings exposure,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct) {
            return Task.Run(() => {
                ct.ThrowIfCancellationRequested();

                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Checking for stub frame..." });

                // Step 1: require the stub file to exist.
                // Do NOT call Notification.ShowError here — we're on a background thread.
                // The VM's catch block is responsible for user notification.
                if (!File.Exists(StubXisfPath)) {
                    throw new FileNotFoundException("XISF stub frame not found at path: " + StubXisfPath, StubXisfPath);
                }

                ct.ThrowIfCancellationRequested();
                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Loading XISF..." });

                // Step 2: attempt to load the file.  XISF is not a format understood
                // by WPF's BitmapDecoder so this will almost always fall through to
                // the synthetic fallback; the important contract is that we tried.
                BitmapSource bitmap = null;
                int width = 0, height = 0;
                try {
                    progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Decoding..." });
                    var uri = new Uri(StubXisfPath, UriKind.Absolute);
                    var decoder = BitmapDecoder.Create(
                        uri,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    bitmap = decoder.Frames[0];
                    bitmap.Freeze();
                    width = bitmap.PixelWidth;
                    height = bitmap.PixelHeight;
                } catch (Exception ex) {
                    Logger.Warning($"XisfStubCaptureSource: could not decode '{StubXisfPath}' via BitmapDecoder ({ex.Message}); using synthetic fallback.");
                    bitmap = null;
                }

                if (bitmap == null) {
                    // Synthetic fallback — keep development unblocked when the real
                    // XISF file is present but not WPF-decodable.
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
                    ? 206.265 * pixelSize / focalLength
                    : 1.0;

                var pv = target.PositionAt(DateTime.UtcNow);
                var coordinates = pv.Coordinates;

                double positionAngle = rng.NextDouble() * 360.0;

                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = "Done" });
                progress?.Report(new ApplicationStatus { Source = "OrbitalFramingWizard", Status = string.Empty });

                return new CapturedFrame {
                    Image = bitmap,
                    WidthPx = width,
                    HeightPx = height,
                    Coordinates = coordinates,
                    PositionAngleDeg = positionAngle,
                    PixscaleArcsecPerPx = pixscale,
                };
            }, ct);
        }

        /// <summary>
        /// Creates a synthetic 640×480 32-bit BGRA bitmap with a simple gradient
        /// so the image panel shows something non-trivial in the UI.
        /// </summary>
        private static BitmapSource CreateSyntheticBitmap(int width, int height) {
            // Use a DrawingVisual to render a gradient.
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen()) {
                var brush = new LinearGradientBrush(
                    Colors.MidnightBlue,
                    Colors.DarkSlateBlue,
                    new Point(0, 0),
                    new Point(1, 1));
                ctx.DrawRectangle(brush, null, new Rect(0, 0, width, height));

                // Draw a simple "star field" of white dots.
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
