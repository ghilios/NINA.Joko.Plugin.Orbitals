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
using NINA.Joko.Plugin.Orbitals.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {

    public class OrbitalElementsObjectTypeUpdatedEventArgs : EventArgs {
        public OrbitalObjectTypeEnum ObjectType { get; set; }
        public int Count { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class VectorTableUpdatedEventArgs : EventArgs {
        public string Name { get; set; }
        public DateTime ValidUntil { get; set; }
    }

    public class OrbitalPositionVelocity {

        public static readonly OrbitalPositionVelocity NotSet = new OrbitalPositionVelocity(
            DateTime.MinValue,
            new RectangularCoordinates(0, 0, 0),
            new Coordinates(Angle.Zero, Angle.Zero, Epoch.J2000),
            SiderealShiftTrackingRate.Disabled);

        public OrbitalPositionVelocity(
            DateTime asof,
            RectangularCoordinates position,
            Coordinates coordinates,
            SiderealShiftTrackingRate trackingRate) {
            this.Asof = asof;
            this.Position = position;
            this.Coordinates = coordinates;
            this.TrackingRate = trackingRate;
        }

        public DateTime Asof { get; private set; }

        public RectangularCoordinates Position { get; private set; }

        public Coordinates Coordinates { get; private set; }

        public SiderealShiftTrackingRate TrackingRate { get; private set; }

        public override string ToString() {
            return $"{{{nameof(Asof)}={Asof.ToString()}, {nameof(Coordinates)}={Coordinates}, {nameof(TrackingRate)}={TrackingRate}}}";
        }
    }

    public interface IOrbitalElementsAccessor {

        Task Load(IProgress<ApplicationStatus> progress, CancellationToken ct);

        IEnumerable<OrbitalElements> Search(OrbitalObjectTypeEnum objectType, string searchString, int? limit = null);

        OrbitalElements Get(OrbitalObjectTypeEnum objectType, string objectName);

        DateTime GetLastUpdated(OrbitalObjectTypeEnum objectType);

        int GetCount(OrbitalObjectTypeEnum objectType);

        Task Update(OrbitalObjectTypeEnum objectType, IEnumerable<IOrbitalElementsSource> elements, IProgress<ApplicationStatus> progress, CancellationToken ct);

        OrbitalPositionVelocity GetSolarSystemBodyPV(DateTime asof, SolarSystemBody solarSystemBody);

        OrbitalPositionVelocity GetObjectPV(DateTime asof, OrbitalElements orbitalElements);

        OrbitalPositionVelocity GetPVFromTable(DateTime asof, PVTable vectorTable);

        Task UpdateJWST(PVTable pvTable, IProgress<ApplicationStatus> progress, CancellationToken ct);

        PVTable GetJWSTVectorTable();

        DateTime GetJWSTValidUntil();

        event EventHandler<OrbitalElementsObjectTypeUpdatedEventArgs> Updated;

        event EventHandler<VectorTableUpdatedEventArgs> VectorTableUpdated;
    }
}