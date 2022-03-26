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

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class PVTableObject : OrbitalsObjectBase {
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IProfileService profileService;

        public PVTableObject(
            IOrbitalElementsAccessor orbitalElementsAccessor,
            string objectName,
            CustomHorizon customHorizon,
            IProfileService profileService) : base(objectName, customHorizon) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.profileService = profileService;
            Moon = new MoonInfo(Coordinates);
        }

        public override MoonInfo Moon { get; protected set; }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            var pvTable = orbitalElementsAccessor.GetJWSTVectorTable();
            if (pvTable != null) {
                var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
                var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
                var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
                return orbitalElementsAccessor.GetPVFromTable(at, pvTable, latitude, longitude, elevation);
            } else {
                return OrbitalPositionVelocity.NotSet;
            }
        }

        public PVTableObject Clone() {
            var cloned = new PVTableObject(orbitalElementsAccessor, this.Name, customHorizon, profileService);
            cloned.SetDateAndPosition(cloned._referenceDate, cloned._latitude, cloned._longitude);
            return cloned;
        }
    }
}