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
}
