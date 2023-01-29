#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class SiderealTrackingRateExtensions {

        public static SiderealShiftTrackingRate AdjustForASCOM(this SiderealShiftTrackingRate rate, IOrbitalsOptions options) {
            var quirksMode = options.QuirksMode;
            switch (quirksMode) {
                case Enums.QuirksModeEnum.None:
                    return SiderealShiftTrackingRate.Create(rate.RADegreesPerHour / AstrometricConstants.SIDEREAL_SEC_PER_SI_SEC, rate.DecDegreesPerHour);

                case Enums.QuirksModeEnum.AstroPhysics:
                    return SiderealShiftTrackingRate.Create(rate.RADegreesPerHour / AstrometricConstants.SIDEREAL_RATE_ARCSEC_PER_SI_SEC, rate.DecDegreesPerHour);

                default:
                    throw new ArgumentException($"{quirksMode} is not an expected Quirk Mode", "options.QuirksMode");
            }
        }
    }
}