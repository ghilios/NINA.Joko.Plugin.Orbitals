#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using System;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    /// <summary>
    /// Backfills astrometry helpers that exist in NINA 3.3 but not in the 3.2 assembly this
    /// branch pins to.
    ///
    /// NINA 3.2's AstroUtil offers only GetJulianDate, which is UTC-based. Several
    /// calculations here need a *Terrestrial Time* julian date -- NOVAS place() and
    /// geo_posvel() both take jd_tt -- and passing a UTC julian date instead costs delta-T,
    /// currently about 69 seconds. That is worth 35 arcsec on the Moon and a couple of
    /// arcsec on the inner planets.
    ///
    /// This is a straight port of NINA 3.3's AstroUtil.GetJulianDateTT, built from the same
    /// SOFA primitives (Dtf2d / UtcTai / TaiTt), all of which 3.2 already exposes. Delete
    /// this file and switch the call sites back to AstroUtil if this branch is ever moved to
    /// a NINA that ships the method.
    /// </summary>
    public static class AstroUtilCompat {

        /// <summary>
        /// Julian date on the TT scale. UTC -> TAI -> TT via SOFA, matching NINA 3.3.
        /// </summary>
        public static double GetJulianDateTT(DateTime date) {
            var (tt, tt2) = GetJulianDateTTParts(date);
            return tt + tt2;
        }

        /// <summary>
        /// Two-part form, kept separate because SOFA returns a split julian date to preserve
        /// precision; callers that only need a single double can use <see cref="GetJulianDateTT"/>.
        /// </summary>
        public static (double, double) GetJulianDateTTParts(DateTime date) {
            var utc = date.ToUniversalTime();
            double utc1 = 0.0, utc2 = 0.0;
            double tai1 = 0.0, tai2 = 0.0;
            double tt1 = 0.0, tt2 = 0.0;

            SOFA.Dtf2d("UTC", utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute,
                GetSecondOfMinuteWithFraction(utc), ref utc1, ref utc2);
            SOFA.UtcTai(utc1, utc2, ref tai1, ref tai2);
            SOFA.TaiTt(tai1, tai2, ref tt1, ref tt2);
            return (tt1, tt2);
        }

        /// <summary>
        /// Seconds within the minute, carrying sub-millisecond precision from the tick count.
        /// NINA 3.2's own DeltaT truncates at milliseconds; this matches 3.3's finer version
        /// so ported calculations behave identically across the two lines.
        /// </summary>
        private static double GetSecondOfMinuteWithFraction(DateTime date) {
            return date.Second + (date.Ticks % TimeSpan.TicksPerSecond) / (double)TimeSpan.TicksPerSecond;
        }
    }
}
