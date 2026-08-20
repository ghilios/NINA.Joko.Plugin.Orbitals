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
            IProfileService profileService) : base(orbitalElements?.Name ?? NotSetName, customHorizon, TimeSpan.FromSeconds(1)) {
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
                    RaisePropertyChanged(nameof(SourceDisplay));
                    RaisePropertyChanged(nameof(HasSource));
                    RaisePropertyChanged(nameof(EpochDisplay));
                }
            }
        }

        /// <summary>
        /// Which dataset these elements came from. Shown next to the object name so the user
        /// can tell at a glance whether they are looking at the fresher MPC entry or a
        /// JPL-only one -- the two can differ by enough to miss the field entirely.
        /// </summary>
        public string SourceDisplay =>
            orbitalElements == null || orbitalElements.Source == OrbitalElementsSourceEnum.Unknown
                ? string.Empty
                : orbitalElements.Source.ToString();

        public bool HasSource => !string.IsNullOrEmpty(SourceDisplay);

        /// <summary>Element epoch as a date, which is the other half of "can I trust this pointing".</summary>
        public string EpochDisplay {
            get {
                if (orbitalElements == null || double.IsNaN(orbitalElements.Epoch_jd)) {
                    return string.Empty;
                }
                return NOVAS.JulianToDateTime(orbitalElements.Epoch_jd).ToString("d", OrbitalsPlugin.SystemCultureInfo);
            }
        }

        protected override OrbitalPositionVelocity CalculateObjectPosition(DateTime at) {
            if (OrbitalElements == null) {
                return OrbitalPositionVelocity.NotSet;
            }
            var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
            var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
            var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
            return orbitalElementsAccessor.GetObjectPV(at, OrbitalElements, latitude, longitude, elevation, rateDriftDelta);
        }

        public OrbitalElementsObject Clone() {
            var cloned = new OrbitalElementsObject(orbitalElementsAccessor, OrbitalElements, customHorizon, profileService);
            cloned.SetDateAndPosition(this._referenceDate, this._latitude, this._longitude);
            return cloned;
        }
    }
}