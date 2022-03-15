using NINA.Astrometry;
using NINA.Core.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {
    public static class Extensions {
        public static NOVAS.Body ToNOVAS(this SolarSystemBody body) {
            return (NOVAS.Body)body;
        }
    }

    public enum SolarSystemBody : short {
        Mercury = 1,
        Venus = 2,
        Earth = 3,
        Mars = 4,
        Jupiter = 5,
        Saturn = 6,
        Uranus = 7,
        Neptune = 8,
        Pluto = 9,
        Sun = 10,
        Moon = 11
    }

    public enum OrbitalObjectType {
        Comet
    }

    public class OrbitalElementsObjectTypeUpdatedEventArgs : EventArgs {
        public OrbitalObjectType ObjectType { get; set; }
        public int Count { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class OrbitalPositionVelocity {
        public OrbitalPositionVelocity(DateTime asof, Coordinates coordinates, SiderealShiftTrackingRate trackingRate) {
            this.Asof = asof;
            this.Coordinates = coordinates;
            this.TrackingRate = trackingRate;
        }

        public DateTime Asof { get; private set; }

        public Coordinates Coordinates { get; private set; }
        
        public SiderealShiftTrackingRate TrackingRate { get; private set; }

        public override string ToString() {
            return $"{{{nameof(Asof)}={Asof.ToString()}, {nameof(Coordinates)}={Coordinates}, {nameof(TrackingRate)}={TrackingRate}}}";
        }
    }

    public interface IOrbitalElementsAccessor {
        Task Load(IProgress<ApplicationStatus> progress, CancellationToken ct);

        IEnumerable<OrbitalElements> Search(OrbitalObjectType objectType, string searchString);

        OrbitalElements Get(OrbitalObjectType objectType, string objectName);

        DateTime GetLastUpdated(OrbitalObjectType objectType);

        int GetCount(OrbitalObjectType objectType);

        Task Update(OrbitalObjectType objectType, IEnumerable<IOrbitalElementsSource> elements, CancellationToken ct);

        OrbitalPositionVelocity GetSolarSystemBodyPV(DateTime asof, SolarSystemBody solarSystemBody);

        OrbitalPositionVelocity GetObjectPV(DateTime asof, OrbitalElements orbitalElements);

        event EventHandler<OrbitalElementsObjectTypeUpdatedEventArgs> Updated;
    }
}
