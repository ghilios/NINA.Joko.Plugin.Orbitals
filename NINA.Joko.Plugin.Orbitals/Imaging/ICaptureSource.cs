using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Calculations;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugin.Orbitals.Imaging {

    /// <summary>
    /// Exposure settings passed to <see cref="ICaptureSource.CaptureAsync"/>.
    /// </summary>
    public sealed class OrbitalFramingExposureSettings {
        public double ExposureTime { get; init; }
        public int Gain { get; init; }
        public int Offset { get; init; }
        public int Binning { get; init; } = 1;
    }

    /// <summary>
    /// Image frame returned by <see cref="ICaptureSource.CaptureAsync"/>.
    /// </summary>
    public sealed class CapturedFrame {
        /// <summary>Raw pixel data for display or astrometric analysis.</summary>
        public required BitmapSource Image { get; init; }

        /// <summary>Frame width in pixels.</summary>
        public int WidthPx { get; init; }

        /// <summary>Frame height in pixels.</summary>
        public int HeightPx { get; init; }

        /// <summary>J2000 equatorial coordinates of the image centre.</summary>
        public required Coordinates Coordinates { get; init; }

        /// <summary>Camera position angle, degrees, North-through-East convention, range [0, 360).</summary>
        public double PositionAngleDeg { get; init; }

        /// <summary>Image scale in arcseconds per pixel.</summary>
        public double PixscaleArcsecPerPx { get; init; }
    }

    /// <summary>
    /// MEF metadata interface used in Phase E for capture-mode selection.
    /// </summary>
    public interface ICaptureSourceMetadata {
        /// <summary>Short identifier for the capture mode (e.g. "Live", "Sequence").</summary>
        string Mode { get; }
    }

    /// <summary>
    /// Abstraction over the camera/capture subsystem used by the framing wizard.
    /// Implementations are injected; no equipment is touched from this assembly.
    /// </summary>
    public interface ICaptureSource {

        /// <summary>
        /// Capture a single frame centred on <paramref name="target"/>.
        /// </summary>
        /// <param name="target">The orbital object whose current coordinates define the pointing.</param>
        /// <param name="exposure">Exposure parameters (time, gain, offset, binning).</param>
        /// <param name="progress">Progress sink for status messages.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="CapturedFrame"/> containing the raw image plus solved astrometric metadata.
        /// </returns>
        Task<CapturedFrame> CaptureAsync(
            OrbitalsObjectBase target,
            OrbitalFramingExposureSettings exposure,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct);
    }
}
