#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using System.IO;
using System.Runtime.InteropServices;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class NOVASEx {
        private const string DLLNAME = "NOVAS31lib.dll";

        private static double JPL_EPHEM_START_DATE = 2305424.5; // First date of data in the ephemerides file
        private static double JPL_EPHEM_END_DATE = 2525008.5; // Last date of data in the ephemerides file

        static NOVASEx() {
            DllLoader.LoadDll(Path.Combine("NOVAS", DLLNAME));

            short a = 0;
            var ephemLocation = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "External", "JPLEPH");
            var code = EphemOpen(ephemLocation, ref JPL_EPHEM_START_DATE, ref JPL_EPHEM_END_DATE, ref a);
            if (code > 0) {
                Logger.Warning($"Failed to load ephemerides file due to error {code}");
            }
        }

        [DllImport(DLLNAME, EntryPoint = "ephem_open", CallingConvention = CallingConvention.Cdecl)]
        private static extern short EphemOpen([MarshalAs(UnmanagedType.LPStr)] string Ephem_Name, ref double JD_Begin, ref double JD_End, ref short DENumber);

        [DllImport(DLLNAME, EntryPoint = "geo_posvel", CallingConvention = CallingConvention.Cdecl)]
        public static extern short NOVAS_geo_posvel(
            double jdtt, double deltaT, NOVAS.Accuracy accuracy, NOVAS.Observer observer,
            [In, Out][MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] pos,
            [In, Out][MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] vel);
    }
}