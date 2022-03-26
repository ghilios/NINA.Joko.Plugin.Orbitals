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
using NINA.Profile.Interfaces;
using System;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class OrbitalElementsObject : OrbitalsObjectBase {
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IProfileService profileService;
        public static readonly string NotSetName = "Orbital Object Sequence";

        public OrbitalElementsObject(
            IOrbitalElementsAccessor orbitalElementsAccessor,
            OrbitalElements orbitalElements,
            CustomHorizon customHorizon,
            IProfileService profileService) : base(orbitalElements?.Name ?? NotSetName, customHorizon) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.orbitalElements = orbitalElements;
            this.profileService = profileService;
            Moon = new MoonInfo(Coordinates);
        }

        public override MoonInfo Moon { get; protected set; }

        private OrbitalElements orbitalElements;

        public OrbitalElements OrbitalElements {
            get => orbitalElements;
            set {
                if (orbitalElements != value) {
                    orbitalElements = value;
                    this.UpdateHorizonAndTransit();
                    RaisePropertyChanged();
                }
            }
        }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            if (OrbitalElements == null) {
                return OrbitalPositionVelocity.NotSet;
            }
            var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
            var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
            var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
            return orbitalElementsAccessor.GetObjectPV(at, OrbitalElements, latitude, longitude, elevation);
        }

        public OrbitalElementsObject Clone() {
            var cloned = new OrbitalElementsObject(orbitalElementsAccessor, OrbitalElements, customHorizon, profileService);
            cloned.SetDateAndPosition(cloned._referenceDate, cloned._latitude, cloned._longitude);
            return cloned;
        }
    }
}