using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Imaging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Tests.TestHelpers {

    /// <summary>
    /// A synchronous in-memory capture source for use in unit tests.
    /// Set <see cref="Next"/> before calling CaptureAsync; the task returns it immediately.
    /// </summary>
    internal class FakeCaptureSource : ICaptureSource {
        /// <summary>The frame that will be returned from the next CaptureAsync call.</summary>
        public CapturedFrame Next { get; set; }

        public Task<CapturedFrame> CaptureAsync(
            OrbitalsObjectBase target,
            OrbitalFramingExposureSettings exposure,
            IProgress<ApplicationStatus> progress,
            CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Next);
        }
    }

    /// <summary>
    /// Minimal <see cref="ICaptureSourceMetadata"/> implementation for test helpers.
    /// </summary>
    internal sealed class FakeCaptureSourceMetadata : ICaptureSourceMetadata {
        public string Mode { get; }
        public FakeCaptureSourceMetadata(string mode) => Mode = mode;
    }

    /// <summary>
    /// Factory helpers for wrapping test capture sources in the Lazy pattern
    /// expected by <see cref="NINA.Joko.Plugin.Orbitals.ViewModels.OrbitalFramingWizardVM"/>.
    /// </summary>
    internal static class CaptureSourceTestHelpers {
        /// <summary>
        /// Wraps a capture source in a <see cref="Lazy{T, TMetadata}"/> with the given mode key.
        /// If <paramref name="mode"/> is null, defaults to "XisfStub" (the fallback mode used
        /// when tests don't need mode-specific selection).
        /// </summary>
        public static Lazy<ICaptureSource, ICaptureSourceMetadata> AsLazy(
            this ICaptureSource source,
            string mode = "XisfStub") {
            return new Lazy<ICaptureSource, ICaptureSourceMetadata>(
                () => source,
                new FakeCaptureSourceMetadata(mode));
        }
    }
}
