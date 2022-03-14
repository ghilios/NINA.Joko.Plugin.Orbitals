using NINA.Astrometry;
using NINA.Core.Utility;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TestApp {
    public static class NOVASEx {
        public static readonly double T0 = 2451545.0;
        public static DateTime J2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        public static DateTime JulianToDateTime(double jdtt) {
            return J2000 + TimeSpan.FromDays(jdtt - T0);
        }

        private const string DLLNAME = "NOVAS31lib.dll";

        private static double JPL_EPHEM_START_DATE = 2305424.5; // First date of data in the ephemerides file
        private static double JPL_EPHEM_END_DATE = 2525008.5; // Last date of data in the ephemerides file
        private const int SIZE_OF_OBJ_NAME = 51;
        private const int SIZE_OF_CAT_NAME = 4;

        static NOVASEx() {
            DllLoader.LoadDll(Path.Combine("NOVAS", DLLNAME));

            short a = 0;
            var ephemLocation = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "External", "JPLEPH");
            var code = EphemOpen(ephemLocation, ref JPL_EPHEM_START_DATE, ref JPL_EPHEM_END_DATE, ref a);
            if (code > 0) {
                Logger.Warning($"Failed to load ephemerides file due to error {code}");
            }
        }

        public enum SolarSystemBody : short {
            Mercury = 1,
            Venus = 2,
            Earth = 3,
            Mars = 4,
            Jupiter = 5,
            Saturn = 6,
            Uranus = 7,
            Neptune = 8,
            Pluto = 9,
            Sun = 10,
            Moon = 11
        }

        public enum SolarSystemOrigin : short {
            Barycenter = 0,
            SolarCenterOfMass = 1
        }

        [DllImport(DLLNAME, EntryPoint = "ephem_open", CallingConvention = CallingConvention.Cdecl)]
        private static extern short EphemOpen([MarshalAs(UnmanagedType.LPStr)] string Ephem_Name, ref double JD_Begin, ref double JD_End, ref short DENumber);

        [DllImport(DLLNAME, EntryPoint = "solarsystem_hp", CallingConvention = CallingConvention.Cdecl)]
        private static extern short SolarSystemHP(
            [In][MarshalAs(UnmanagedType.LPArray, SizeConst = 2)] double[] tjd,
            SolarSystemBody body,
            SolarSystemOrigin origin,
            [In, Out][MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] position,
            [In, Out][MarshalAs(UnmanagedType.LPArray, SizeConst = 3)] double[] velocity);

        public static RectangularPV SolarSystemBodyPV(double jdtt, SolarSystemBody body, SolarSystemOrigin origin) {
            var jd = new double[] { jdtt, 0 };
            var position = new double[3];
            var velocity = new double[3];
            var result = SolarSystemHP(jd, body, origin, position, velocity);
            if (result != 0) {
                throw new Exception($"SolarSystemBodyPV failed for {body} with origin {origin}. Result={result}");
            }

            return new RectangularPV(
                new RectangularCoordinates(position[0], position[1], position[2]),
                new RectangularCoordinates(velocity[0], velocity[1], velocity[2]));
        }

        [DllImport(DLLNAME, EntryPoint = "make_cat_entry", CallingConvention = CallingConvention.Cdecl)]
        private static extern short MakeCatEntry(
            [MarshalAs(UnmanagedType.LPTStr, SizeConst = SIZE_OF_OBJ_NAME)] string star_name,
            [MarshalAs(UnmanagedType.LPTStr, SizeConst = SIZE_OF_CAT_NAME)] string catalog,
            long star_num,
            double ra,
            double dec,
            double pm_ra,
            double pm_dec,
            double parallax,
            double rad_vel,
            [Out] out NOVAS.CatalogueEntry star);

        private static readonly Lazy<NOVAS.CatalogueEntry> dummy_star = new Lazy<NOVAS.CatalogueEntry>(() => {
            var result = MakeCatEntry("DUMMY", "xxx", 0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, out var output);
            if (result != 0) {
                throw new Exception($"Failed to create dummy star cat entry. Result={result}");
            }
            return output;
        });

        public enum ObjectType : short {
            MajorPlanet = 0,
            MinorPlanet = 1,
            ExtraSolarObject = 2
        }

        [DllImport(DLLNAME, EntryPoint = "make_object", CallingConvention = CallingConvention.Cdecl)]
        private static extern short MakeObject(
            ObjectType type,
            short number,
            [MarshalAs(UnmanagedType.LPTStr, SizeConst = SIZE_OF_OBJ_NAME)] string name,
            NOVAS.CatalogueEntry star_data,
            [Out] out NOVAS.CelestialObject cel_obj);

        public enum Accuracy : short {
            Full = 0,
            Reduced = 1
        }

        [DllImport(DLLNAME, EntryPoint = "app_planet", CallingConvention = CallingConvention.Cdecl)]
        private static extern short AppPlanet(
            double jd_tt,
            NOVAS.CelestialObject ss_body,
            Accuracy accuracy,
            [Out] out double ra,
            [Out] out double dec,
            [Out] out double dis);

        public static Coordinates GetApparentCoordinates(double jd_tt, SolarSystemBody body, Accuracy accuracy = Accuracy.Full) {
            var result = MakeObject(ObjectType.MajorPlanet, (short)body, body.ToString(), dummy_star.Value, out var celestialObject);
            if (result != 0) {
                throw new Exception($"Failed MakeObject for {body}. Result={result}");
            }

            result = AppPlanet(jd_tt, celestialObject, accuracy, out var ra, out var dec, out var _);
            if (result != 0) {
                throw new Exception($"Failed AppPlanet for {body}. Result={result}");
            }

            var referenceDateTime = JulianToDateTime(jd_tt);
            return new Coordinates(Angle.ByHours(ra), Angle.ByDegree(dec), Epoch.JNOW, referenceDateTime);
        }
    }
}
