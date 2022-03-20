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
using System;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class PVTableObject : OrbitalsObjectBase {
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;

        public PVTableObject(
            IOrbitalElementsAccessor orbitalElementsAccessor,
            string objectName,
            CustomHorizon customHorizon) : base(objectName, customHorizon) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            Moon = new MoonInfo(Coordinates);
        }

        public override MoonInfo Moon { get; protected set; }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            var pvTable = orbitalElementsAccessor.GetJWSTVectorTable();
            if (pvTable != null) {
                return orbitalElementsAccessor.GetPVFromTable(at, pvTable);
            } else {
                return OrbitalPositionVelocity.NotSet;
            }
        }

        public PVTableObject Clone() {
            var cloned = new PVTableObject(orbitalElementsAccessor, this.Name, customHorizon);
            cloned.SetDateAndPosition(cloned._referenceDate, cloned._latitude, cloned._longitude);
            return cloned;
        }
    }
}