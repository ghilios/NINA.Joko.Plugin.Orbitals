using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.TestHelpers {

    /// <summary>
    /// A minimal concrete implementation of <see cref="OrbitalsObjectBase"/> for unit tests.
    /// Returns a fixed position and tracking rate supplied at construction time.
    /// </summary>
    internal sealed class FakeOrbitalsObject : OrbitalsObjectBase {
        private readonly OrbitalPositionVelocity fixedPosition;
        private readonly MoonInfo moon;

        public FakeOrbitalsObject(
            string name,
            Coordinates coordinates,
            SiderealShiftTrackingRate trackingRate)
            : base(name, customHorizon: null, rateDriftDelta: TimeSpan.FromSeconds(1)) {
            fixedPosition = new OrbitalPositionVelocity(
                DateTime.UtcNow,
                new RectangularCoordinates(1, 0, 0),
                new TopocentricCoordinates(Angle.Zero, Angle.Zero, Angle.Zero, Angle.Zero, 0.0),
                coordinates,
                trackingRate);
            moon = new MoonInfo(coordinates);
        }

        public override MoonInfo Moon {
            get => moon;
            protected set { /* no-op for tests */ }
        }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            return fixedPosition;
        }
    }
}
