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
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {
    public class OrbitalElementsObject : OrbitalsObjectBase {
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        public static readonly string NotSetName = "Orbital Object Sequence";
        public OrbitalElementsObject(
            IOrbitalElementsAccessor orbitalElementsAccessor,
            OrbitalElements orbitalElements,
            CustomHorizon customHorizon) : base(orbitalElements?.Name ?? NotSetName, customHorizon) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.OrbitalElements = orbitalElements;
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
            return orbitalElementsAccessor.GetObjectPV(at, OrbitalElements);
        }

        public OrbitalElementsObject Clone() {
            var cloned = new OrbitalElementsObject(orbitalElementsAccessor, OrbitalElements, customHorizon);
            cloned.SetDateAndPosition(cloned._referenceDate, cloned._latitude, cloned._longitude);
            return cloned;
        }
    }
}