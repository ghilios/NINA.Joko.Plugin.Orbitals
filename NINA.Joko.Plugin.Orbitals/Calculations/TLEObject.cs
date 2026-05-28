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
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Profile.Interfaces;
using SGPdotNET.CoordinateSystem;
using SGPdotNET.Observation;
using SGPdotNET.TLE;
using System;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class TLEObject : OrbitalsObjectBase {
        private readonly IProfileService profileService;
        private readonly double elevation;
        private readonly Angle latitude;
        private readonly Angle longitude;
        private readonly GeodeticCoordinate location;
        private readonly Epoch epoch;
        private Satellite satellite;

        public TLEObject(
            Tle tle,
            CustomHorizon customHorizon,
            IProfileService profileService,
            Epoch epoch,
            TimeSpan rateDriftDelta) : base(tle?.Name ?? "", customHorizon, rateDriftDelta) {
            if (tle != null) {
                this.satellite = new Satellite(tle);
            }

            this.profileService = profileService;
            this.elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
            this.latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
            this.longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
            this.location = new GeodeticCoordinate(
                SGPdotNET.Util.Angle.FromDegrees(profileService.ActiveProfile.AstrometrySettings.Latitude),
                SGPdotNET.Util.Angle.FromDegrees(profileService.ActiveProfile.AstrometrySettings.Longitude),
                elevation);
            this.epoch = epoch;
            Moon = new MoonInfo(Coordinates);
        }

        public Tle Tle {
            get => this.satellite?.Tle;
            set {
                if (value == null) {
                    this.satellite = null;
                } else {
                    this.satellite = new Satellite(value);
                }
                this.UpdateHorizonAndTransit();
                RaiseAllPropertiesChanged();
            }
        }

        public override MoonInfo Moon { get; protected set; }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            if (this.satellite == null) {
                return OrbitalPositionVelocity.NotSet;
            }

            var startCoordinates = CalculateCoordinatesAt(at);
            var endCoordinates = CalculateCoordinatesAt(at + rateDriftDelta);
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates.coordinates, endCoordinates.coordinates, rateDriftDelta);
            return new OrbitalPositionVelocity(
                at,
                startCoordinates.position,
                startCoordinates.topoCoordinates,
                startCoordinates.coordinates,
                trackingRate);
        }

        private record CalculatedCoordinates(TopocentricCoordinates topoCoordinates, Coordinates coordinates, RectangularCoordinates position);

        private CalculatedCoordinates CalculateCoordinatesAt(DateTime at) {
            at = at.ToUniversalTime();
            EciCoordinate eciCoordinate = satellite.Predict(at);
            var observation = location.Observe(eciCoordinate, at);
            var topoCoordinates = new TopocentricCoordinates(
                    Angle.ByRadians(observation.Azimuth.Radians),
                    Angle.ByRadians(observation.Elevation.Radians),
                    latitude,
                    longitude,
                    elevation,
                    new FixedDateTime(at));
            var coordinates = topoCoordinates.Transform(epoch);
            var position = new RectangularCoordinates(
                eciCoordinate.Position.X / AstrometricConstants.KM_PER_AU,
                eciCoordinate.Position.Y / AstrometricConstants.KM_PER_AU,
                eciCoordinate.Position.Z / AstrometricConstants.KM_PER_AU);
            return new CalculatedCoordinates(topoCoordinates, coordinates, position);
        }

        public TLEObject Clone() {
            var cloned = new TLEObject(satellite?.Tle, customHorizon, profileService, epoch, rateDriftDelta);
            cloned.SetDateAndPosition(this._referenceDate, this._latitude, this._longitude);
            return cloned;
        }
    }
}