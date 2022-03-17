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
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class SolarSystemBodyObject : OrbitalsObjectBase {
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;

        public SolarSystemBodyObject(
            IOrbitalElementsAccessor orbitalElementsAccessor,
            SolarSystemBody solarSystemBody,
            CustomHorizon customHorizon) : base(solarSystemBody.ToString(), customHorizon) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.solarSystemBody = solarSystemBody;
            Moon = new MoonInfo(Coordinates);
        }

        public override MoonInfo Moon { get; protected set; }

        private SolarSystemBody solarSystemBody;

        public SolarSystemBody SolarSystemBody {
            get => solarSystemBody;
            set {
                if (solarSystemBody != value) {
                    solarSystemBody = value;
                    this.UpdateHorizonAndTransit();
                    RaisePropertyChanged();
                }
            }
        }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            return orbitalElementsAccessor.GetSolarSystemBodyPV(at, SolarSystemBody);
        }

        public SolarSystemBodyObject Clone() {
            var cloned = new SolarSystemBodyObject(orbitalElementsAccessor, SolarSystemBody, customHorizon);
            cloned.SetDateAndPosition(cloned._referenceDate, cloned._latitude, cloned._longitude);
            return cloned;
        }
    }
}