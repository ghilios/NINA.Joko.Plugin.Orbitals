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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestApp {

    public class SOFAEx {
        private const string DLLNAME = "SOFAlib.dll";
        public static readonly DateTime J2000DT = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly Lazy<Angle> j2000MeanObliquity = new Lazy<Angle>(() => MeanEcclipticObliquity(J2000DT));

        static SOFAEx() {
            DllLoader.LoadDll(Path.Combine("SOFA", DLLNAME));
        }

        [DllImport(DLLNAME, EntryPoint = "iauObl80", CallingConvention = CallingConvention.Cdecl)]
        public static extern double SOFA_iauObl80(double date1, double date2);

        [DllImport(DLLNAME, EntryPoint = "iauEpv00", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SOFA_iauEpv00(
            double date1,
            double date2,
            [In, Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4)] double[,] pvh, // 2x3
            [In, Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4)] double[,] pvd); // 2x3

        public static Angle MeanEcclipticObliquity(DateTime asof) {
            double tai1 = 0, tai2 = 0, tt1 = 0, tt2 = 0;
            var jd = AstroUtil.GetJulianDate(asof);

            SOFA.UtcTai(jd, 0.0, ref tai1, ref tai2);
            SOFA.TaiTt(tai1, tai2, ref tt1, ref tt2);

            var meanObliquity = SOFAEx.SOFA_iauObl80(tt1, tt2);
            return Angle.ByRadians(meanObliquity);
        }

        public static Angle J2000MeanObliquity => j2000MeanObliquity.Value;
    }
}