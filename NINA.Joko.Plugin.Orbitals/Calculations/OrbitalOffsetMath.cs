using NINA.Astrometry;
using System;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    /// <summary>
    /// Pure static helpers for great-circle angular separations, position angles,
    /// and offset coordinate computation.  All methods operate on the celestial
    /// sphere using exact spherical-trig formulae (no small-angle approximations).
    /// </summary>
    public static class OrbitalOffsetMath {

        private const double ArcsecPerRadian = (180.0 * 3600.0) / Math.PI;
        private const double RadiansPerHour = Math.PI / 12.0;
        private const double RadiansPerDegree = Math.PI / 180.0;
        private const double DegreesPerRadian = 180.0 / Math.PI;

        /// <summary>
        /// Angular separation between two sky positions using the haversine formula.
        /// </summary>
        /// <param name="c1">First sky position (RA in hours, Dec in degrees, J2000).</param>
        /// <param name="c2">Second sky position (RA in hours, Dec in degrees, J2000).</param>
        /// <returns>Angular separation in arcseconds.</returns>
        public static double AngularSeparation(Coordinates c1, Coordinates c2) {
            double ra1 = c1.RA * RadiansPerHour;
            double dec1 = c1.Dec * RadiansPerDegree;
            double ra2 = c2.RA * RadiansPerHour;
            double dec2 = c2.Dec * RadiansPerDegree;

            double dDec = dec2 - dec1;
            double dRa = ra2 - ra1;

            double sinHalfDDec = Math.Sin(dDec / 2.0);
            double sinHalfDRa = Math.Sin(dRa / 2.0);

            double a = sinHalfDDec * sinHalfDDec
                     + Math.Cos(dec1) * Math.Cos(dec2) * sinHalfDRa * sinHalfDRa;

            // Clamp a to [0,1] to guard against floating-point noise at a=1
            a = Math.Min(1.0, Math.Max(0.0, a));

            double sepRad = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return sepRad * ArcsecPerRadian;
        }

        /// <summary>
        /// Position angle of <paramref name="to"/> as seen from <paramref name="from"/>,
        /// measured North-through-East (standard astronomical convention).
        /// </summary>
        /// <param name="from">Origin position (RA in hours, Dec in degrees).</param>
        /// <param name="to">Target position (RA in hours, Dec in degrees).</param>
        /// <returns>Position angle in degrees in the range [0, 360).</returns>
        public static double PositionAngleNToE(Coordinates from, Coordinates to) {
            double ra1 = from.RA * RadiansPerHour;
            double dec1 = from.Dec * RadiansPerDegree;
            double ra2 = to.RA * RadiansPerHour;
            double dec2 = to.Dec * RadiansPerDegree;

            double dRa = ra2 - ra1;

            double y = Math.Sin(dRa) * Math.Cos(dec2);
            double x = Math.Cos(dec1) * Math.Sin(dec2) - Math.Sin(dec1) * Math.Cos(dec2) * Math.Cos(dRa);

            double paRad = Math.Atan2(y, x);
            double paDeg = paRad * DegreesPerRadian;

            // Normalise to [0, 360)
            paDeg = (paDeg % 360.0 + 360.0) % 360.0;
            return paDeg;
        }

        /// <summary>
        /// Applies a polar-offset (separation + position angle) to a sky coordinate.
        /// The returned <see cref="Coordinates"/> inherits <paramref name="from"/>'s
        /// epoch so callers that track JNow targets (e.g. ManualTLEContainer) don't
        /// silently get their epoch flipped to J2000 by the slew math.
        /// </summary>
        /// <param name="from">Starting position (RA in hours, Dec in degrees).</param>
        /// <param name="separationArcsec">Angular separation in arcseconds.</param>
        /// <param name="positionAngleDeg">Position angle in degrees, North-through-East.</param>
        /// <returns>
        /// A new <see cref="Coordinates"/> displaced from <paramref name="from"/> by
        /// the given separation and position angle, in the same epoch as
        /// <paramref name="from"/>.
        /// </returns>
        public static Coordinates ApplyOffset(Coordinates from, double separationArcsec, double positionAngleDeg) {
            double dec1 = from.Dec * RadiansPerDegree;
            double ra1 = from.RA * RadiansPerHour;
            double sepRad = separationArcsec / ArcsecPerRadian;
            double paRad = positionAngleDeg * RadiansPerDegree;

            double sinDec1 = Math.Sin(dec1);
            double cosDec1 = Math.Cos(dec1);
            double sinSep = Math.Sin(sepRad);
            double cosSep = Math.Cos(sepRad);

            double sinDec2 = sinDec1 * cosSep + cosDec1 * sinSep * Math.Cos(paRad);
            // Clamp for numerical safety at poles
            sinDec2 = Math.Min(1.0, Math.Max(-1.0, sinDec2));
            double dec2 = Math.Asin(sinDec2);

            double dRa = Math.Atan2(
                Math.Sin(paRad) * sinSep * cosDec1,
                cosSep - sinDec1 * sinDec2);

            double ra2 = ra1 + dRa;

            // Convert back: ra2 is in radians (hours * π/12), dec2 in radians
            double ra2Hours = ra2 / RadiansPerHour;
            double dec2Deg = dec2 * DegreesPerRadian;

            // Normalise RA to [0, 24)
            ra2Hours = (ra2Hours % 24.0 + 24.0) % 24.0;

            return new Coordinates(Angle.ByHours(ra2Hours), Angle.ByDegree(dec2Deg), from.Epoch);
        }
    }
}
