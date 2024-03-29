﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public static class AstrometricConstants {
        public const double SUN_MU = 1.32712440018e20; // Sun gravitational parameter, m^3/s^2
        public const double EARTH_MU = 3.986004418e14; // Earth gravitational parameter, m^3/s^2
        public const long SEC_PER_DAY = 60L * 60L * 24L;
        public const double JD_SEC = 1.0d / SEC_PER_DAY;
        public const double KM_PER_AU = 1.49597870691e8;
        public const double M_PER_AU = 1.49597870691e11;

        // Conversion factor for m^3/s^2 to km^3/d^2
        public const double M3_S2_TO_KM3_D2_FACTOR = SEC_PER_DAY * SEC_PER_DAY / 1000000000.0d;

        public const double M3_S2_TO_AU3_D2_FACTOR = SEC_PER_DAY * SEC_PER_DAY / (M_PER_AU * M_PER_AU * M_PER_AU);

        public const double SUN_MU_KMD = SUN_MU * M3_S2_TO_KM3_D2_FACTOR;
        public const double RAD_PER_DEG = Math.PI / 180d;
        public const double TWO_PI = 2d * Math.PI;

        public const double SIDEREAL_SEC_PER_SI_SEC = 1.00273791552838d;
        public const double SIDEREAL_RATE_ARCSEC_PER_SI_SEC = 15.0410686d;

        /// <summary>
        /// Mean ecliptic obliquity at J2000
        /// </summary>
        public static readonly Angle J2000MeanObliquity = SOFA.MeanEcclipticObliquity(SOFA.J2000_jd, 0.0);

        public static double NormalizeRadians(double r) {
            r = r % TWO_PI;
            if (r < 0) {
                r += TWO_PI;
            }
            if (r > Math.PI) {
                r -= TWO_PI;
            }
            return r;
        }
    }
}