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
using System;
using System.Runtime.InteropServices;

namespace TestApp {

    internal class OrbitalProgram {

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        // TODO: Get constants from https://github.com/brandon-rhodes/python-novas/blob/master/novas_py/constants.py
        public class JPLOsculatingOrbitalElements {

            public JPLOsculatingOrbitalElements(string name) {
                this.Name = name;
            }

            public string Name { get; private set; }
            public double Epoch_jd { get; set; }
            public double e { get; set; } // Eccentricity
            public double q_km { get; set; } // Periapsis distance
            public double i_deg { get; set; } // Inclination
            public double Omega_deg { get; set; } // Longitude of ascending node
            public double w_deg { get; set; } // Argument of perihelion
            public double PeriapsisTime_jd { get; set; } // Tp
            public double n_degPerSec { get; set; } // Mean Motion
            public double M_deg { get; set; } // Mean anomaly
            public double nu_deg { get; set; } // True anomaly
            public double a_km { get; set; } // Semi-major axis
            public double AD_km { get; set; } // Apoapsis distance
            public double Period_sec { get; set; } // Sidereal orbit period
        }

        private static void Main2(string[] args) {
            // var result = AephemDll.ae_ctime_to_jd(10.0);
            // var result2 = AephemDll.ae_coord_to_constel_index(10.0, 10.0, 10.0);

            /*
             * JPL to aephem
             * epoch = Epoch - 2400000.5.
            inclination = i
            node = Node
            argperih = w
            meandistance = ??? q
            dailymotion = 0
            eccentricity = e
            meananomaly = 0
            equinox = Julian date, converted from Tp which is YYYYmmdd. format

            Unaccounted = Tp, q (perihelion distance)
            */

            /*
             * C/2021 O3 (PANSTARRS)2
             2459640.500000000 = A.D. 2022-Mar-02 00:00:00.0000 TDB
            EC= 1.000102179259416E+00 QR= 4.298294310647514E+07 IN= 5.679078198631460E+01
            OM= 1.890199156770729E+02 W = 2.999909482633409E+02 Tp=  2459690.547058001626
            N = 7.650297992246166E-11 MA=-3.308039199472741E-04 TA= 2.368355759541053E+02
            A =-4.206621123681946E+11 AD= 9.999999999999998E+99 PR= 9.999999999999998E+99

              Ecliptic at the standard reference epoch

            Reference epoch: J2000.0
            X-Y plane: adopted Earth orbital plane at the reference epoch
               Note: IAU76 obliquity of 84381.448 arcseconds wrt ICRF X-Y plane
            X-axis   : ICRF
            Z-axis   : perpendicular to the X-Y plane in the directional (+ or -) sense
               of Earth's north pole at the reference epoch.

            Symbol meaning:

            JDTDB    Julian Day Number, Barycentric Dynamical Time
            EC     Eccentricity, e
            QR     Periapsis distance, q (km)
            IN     Inclination w.r.t X-Y plane, i (degrees)
            OM     Longitude of Ascending Node, OMEGA, (degrees)
            W      Argument of Perifocus, w (degrees)
            Tp     Time of periapsis (Julian Day Number)
            N      Mean motion, n (degrees/sec)
            MA     Mean anomaly, M (degrees)
            TA     True anomaly, nu (degrees)
            A      Semi-major axis, a (km)
            AD     Apoapsis distance (km)
            PR     Sidereal orbit period (sec)
            */

            /*
             * CK21O030  2022 04 21.0473  0.287321  1.000102  299.9913  189.0196   56.7914  20220305  10.5  4.0  C/2021 O3 (PANSTARRS)                                    MPEC 2022-C56
             * */
            var dt = new DateTime(2022, 4, 21, 0, 0, 0, DateTimeKind.Utc);
            dt += TimeSpan.FromDays(0.04087);
            var unixEpoch = new DateTime(1970, 1, 1);
            var dt_jd_tt = AstroUtil.GetJulianDate(DateTime.UtcNow);

            // C/2021 O3 (PANSTARRS)                      59494  0.28730571 1.00013739  56.76016 299.98139 189.03467 20220421.04087 JPL 15
            // q (perihelion) = 0.287
            // e (eccentricity) = 1.000137
            // i (inclination, deg) = 56.76016
            // w (arg of perihelion) = 299.98139
            // Node (longitude of ascending) = 189.03467
            // Tp (periapsis time) = 20220421.04087
            /*
            var C2021_O3_Panstarrs = new JPLOsculatingOrbitalElements("C/2021 O3 PANSTARRS") {
                Epoch_jd = 59494 + 2400000.5,
                e = 1.00013739,
                q_km = 0.28730571 * AE_AU,
                i_deg = 56.76016,
                Omega_deg = 189.03467,
                w_deg = 299.98139,
                PeriapsisTime_jd = dt_jd_tt,
                M_deg = -3.308039199472741E-04,
                // a_km = -4.206621123681946E+11,
                a_km = 0.28730571 / (1.00013739 - 1) * AE_AU
            };
            */
            var C2021_O3_Panstarrs = new JPLOsculatingOrbitalElements("C/2021 O3 PANSTARRS") {
                Epoch_jd = 2459640.500000000,
                e = 1.000102179259416E+00,
                q_km = 4.298294310647514E+07,
                i_deg = 5.679078198631460E+01,
                Omega_deg = 1.890199156770729E+02,
                w_deg = 2.999909482633409E+02,
                PeriapsisTime_jd = 2459690.547058001626,
                n_degPerSec = 7.650297992246166E-11,
                M_deg = -3.308039199472741E-04,
                nu_deg = 2.368355759541053E+02,
                // a_km = -4.206621123681946E+11,
                a_km = 4.206621123681946E+11,
                AD_km = 9.999999999999998E+99,
                Period_sec = 9.999999999999998E+99
            };
            var V2020_O3_Panstarrs_OE = new OrbitalElements("C/2021 O3 PANSTARRS") {
                EpochJulianDate = 2459640.500000000,
                PeriapsisDistanceKm = 4.298294310647514E+07,
                ArgumentOfPerihelion = Angle.ByDegree(2.999909482633409E+02),
                LongitudeOfAscendingNode = Angle.ByDegree(1.890199156770729E+02),
                Eccentricity = 1.000102179259416E+00,
                TimeOfPerihelionJulianDate = 2459690.547058001626,
                Inclination = Angle.ByDegree(5.679078198631460E+01)
            };
            var earth_OE = new OrbitalElements("Earth") {
                EpochJulianDate = 2459640.500000000,
                PeriapsisDistanceKm = 1.471539498803332E+08,
                ArgumentOfPerihelion = Angle.ByDegree(2.941803879374423E+02),
                LongitudeOfAscendingNode = Angle.ByDegree(1.713520303158736E+02),
                Eccentricity = 1.725578060890901E-02,
                TimeOfPerihelionJulianDate = 2459585.712741865776,
                Inclination = Angle.ByDegree(2.673530405875552E-03),
                GravitationalParameterM3S2 = EARTH_MU
            };

            /*
            var q = new double[3];
            var v = new double[3];
            IntPtr hModule = LoadLibrary(AephemDll.DLLNAME);
            IntPtr hVariable = GetProcAddress(hModule, "ae_orb_earth");
            var earthOrbital = new ae_orbit_t();
            Marshal.PtrToStructure(hVariable, earthOrbital);
            var jdtt = AstroUtil.GetJulianDate(DateTime.UtcNow);
            AephemDll.ae_kepler(jdtt, earthOrbital, q);
            AephemDll.ae_v_orbit(jdtt, earthOrbital, v);
            */

            var now = DateTime.UtcNow;
            var eclipticPositionCoordinates = SolveKeplerAt(V2020_O3_Panstarrs_OE, now);
            var earthPositionCoordinates = SolveKeplerAt(earth_OE, now);

            // Convert to J2000 ecliptic - https://ssd.jpl.nasa.gov/planets/approx_pos.html
            var geometricPositionCoordinates = (eclipticPositionCoordinates - earthPositionCoordinates).RotateEcliptic(AstrometricConstants.J2000MeanObliquity);
            var distance_km = geometricPositionCoordinates.Distance;
            var distance_au = distance_km / AE_AU;
            var geometricPolarCoordinates = geometricPositionCoordinates.ToPolar();

            Console.WriteLine();

            // f is center of ellipse to either focus
            // a is semi-major axis (a)
            // f = a - perihelion_distance
            // Eccentricity = f / a

            // Periapsis = a(1-e)
            // e = 1 - (p / a)

            /*
            var f = C2021_O3_Panstarrs.a_km * C2021_O3_Panstarrs.e;
            var perihelion_distance = C2021_O3_Panstarrs.a_km - f;
            var f2 = C2021_O3_Panstarrs.a_km * (C2021_O3_Panstarrs.e - 1);

            // q = a(e-1)

            var orbit = C2021_O3_Panstarrs.ToAephemOrbit();

            // string path = @"C:\Users\ghili\Downloads\ELEMENTS.COMET";
            // string cometName = "JPL J863/77";
            // var result = AephemDll.ae_read_orbit_from_cat(path, cometName, out var orbit);

            var q = new double[3];
            var v = new double[3];
            var star = new ae_star_t();

            IntPtr hModule = LoadLibrary(AephemDll.DLLNAME);
            IntPtr hVariable = GetProcAddress(hModule, "ae_orb_earth");

            var earthOrbital = new ae_orbit_t();
            Marshal.PtrToStructure(hVariable, earthOrbital);

            AephemDll.ae_kepler(jd_tt, orbit, q);
            AephemDll.ae_v_orbit(jd_tt, orbit, v);
            AephemDll.ae_geocentric_from_orbit(jd_tt, earthOrbital, orbit, out var geo_ra, out var geo_dec, out var dist);
            Console.WriteLine();
            */
        }

        private const double GAUSSIAN_CONSTANT = 0.01720209895d; // k, radians
        private const double SUN_MU = 1.32712440018e20; // Sun gravitational parameter, m^3/s^2
        private const double EARTH_MU = 3.986004418e14; // Earth gravitational parameter, m^3/s^2
        private const long SEC_PER_DAY = 60L * 60L * 24L;

        /// <summary>
        /// Astronomical unit in km
        /// </summary>
        public const double AE_AU = 1.49597870691e8;

        // Conversion factor for m^3/s^2 to km^3/d^2
        private const double M3_S2_TO_KM3_D2_FACTOR = SEC_PER_DAY * SEC_PER_DAY / 1000000000.0d;

        private const double SUN_MU_KMD = SUN_MU * M3_S2_TO_KM3_D2_FACTOR;
        private const double RAD_PER_DEG = Math.PI / 180d;
        private const double TWO_PI = 2d * Math.PI;

        private static Angle NormalizeRadians(double r) {
            r = r % TWO_PI;
            if (r < 0) {
                r += TWO_PI;
            }
            if (r > Math.PI) {
                r -= TWO_PI;
            }
            return Angle.ByRadians(r);
        }

        // TODO: Add Osculating elements - http://www.stargazing.net/kepler/ellipse.html#twig02
        //       JPL offers these
        public class OrbitalElements {

            public OrbitalElements(string name) {
                this.Name = name;
            }

            public string Name { get; private set; }
            public double EpochJulianDate { get; set; }
            public double PeriapsisDistanceKm { get; set; }
            public Angle ArgumentOfPerihelion { get; set; }
            public double Eccentricity { get; set; }
            public double TimeOfPerihelionJulianDate { get; set; }
            public Angle LongitudeOfAscendingNode { get; set; }
            public Angle Inclination { get; set; }
            public double GravitationalParameterM3S2 { get; set; } = 0d; // m^3/s^2
        }

        public class RectangularCoordinates {

            public RectangularCoordinates(Epoch epoch, double x, double y, double z) {
                this.Epoch = epoch;
                this.X = x;
                this.Y = y;
                this.Z = z;
            }

            public Epoch Epoch { get; private set; }
            public double X { get; private set; }
            public double Y { get; private set; }
            public double Z { get; private set; }
            public double Distance => Math.Sqrt(X * X + Y * Y + Z * Z);

            public RectangularCoordinates RotateEcliptic(Angle meanObliquity) {
                var meanObliquityRad = meanObliquity.Radians;
                var x = this.X;
                var y = this.Y * Math.Cos(meanObliquityRad) - this.Z * Math.Sin(meanObliquityRad);
                var z = this.Y * Math.Sin(meanObliquityRad) + this.Z * Math.Cos(meanObliquityRad);
                return new RectangularCoordinates(Epoch, x, y, z);
            }

            public static RectangularCoordinates operator -(RectangularCoordinates l, RectangularCoordinates r) {
                if (l.Epoch != r.Epoch) {
                    throw new ArgumentException();
                }

                return new RectangularCoordinates(
                    l.Epoch,
                    l.X - r.X,
                    l.Y - r.Y,
                    l.Z - r.Z);
            }

            public static RectangularCoordinates operator *(RectangularCoordinates l, double mult) {
                return new RectangularCoordinates(
                    l.Epoch,
                    l.X * mult,
                    l.Y * mult,
                    l.Z * mult);
            }

            public static RectangularCoordinates operator /(RectangularCoordinates l, double div) {
                return new RectangularCoordinates(
                    l.Epoch,
                    l.X / div,
                    l.Y / div,
                    l.Z / div);
            }

            public Coordinates ToPolar() {
                var ra = Angle.ByRadians(Math.Atan2(this.Y, this.X));
                var dec = Angle.ByRadians(Math.Asin(this.Z / this.Distance));
                return new Coordinates(ra: ra, dec: dec, epoch: this.Epoch);
            }

            public override string ToString() {
                return $"{{{nameof(Epoch)}={Epoch.ToString()}, {nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(Z)}={Z.ToString()}}}";
            }
        }

        private static RectangularCoordinates SolveKeplerAt(OrbitalElements orbital, DateTime asof) {
            // TODO: Osculation factors in perturbation and should be leveraged instead.
            //       REFERENCE = http://www.stargazing.net/kepler/ellipse.html

            var jdtt = AstroUtil.GetJulianDate(asof);
            var ecc = orbital.Eccentricity;

            // r1, r2 = perihelion, aphelion
            // r1 = a(1 - e)
            // r2 = a(1 + e)
            // r1 + r2 = 2a
            var semiMajorAxis = orbital.PeriapsisDistanceKm / (1 - ecc);
            if (ecc > 1) {
                semiMajorAxis *= -1;
            }

            // r = distance to body from focus
            // v = angle from perihelion to position

            // h^2 = mu * a * (1 - e^2)
            var mu = SUN_MU_KMD + orbital.GravitationalParameterM3S2 * M3_S2_TO_KM3_D2_FACTOR;
            var h = Math.Sqrt(mu * semiMajorAxis * Math.Abs(1.0d - ecc * ecc));
            // n^2 * a^3 = mu
            var n2 = mu / (semiMajorAxis * semiMajorAxis * semiMajorAxis);
            var n = Math.Sqrt(n2);

            var daysSincePeriapsis = jdtt - orbital.TimeOfPerihelionJulianDate;
            // TODO: Take modulus so between -PI, PI, if bound orbit
            var meanAnomaly = n * daysSincePeriapsis; // radians

            // Newtown-Raphson to solve for the eccentric anomaly. Spherical Astronomy 6.4
            var estimate = meanAnomaly + ecc * Math.Sin(meanAnomaly);
            var tolerance = 1.0e-11;
            double estimateError = double.PositiveInfinity;

            // TODO: Add bounds to detect non-convergence
            if (ecc < 1) {
                // Solve M = E - e * sin(E), for E
                while (Math.Abs(estimateError) > tolerance) {
                    estimateError = estimate - ecc * Math.Sin(estimate) - meanAnomaly;
                    estimate -= estimateError / (1.0d - ecc * Math.Cos(estimate));
                }
            } else if (ecc > 1) {
                // Solve M = e * sinh(E) - E, for E
                while (Math.Abs(estimateError) > tolerance) {
                    estimateError = meanAnomaly + estimate - ecc * Math.Sinh(estimate);
                    estimate += estimateError / (ecc * Math.Cosh(estimate) - 1.0d);
                }
            }
            var eccentricAnomaly = estimate;

            double distance = double.NaN;
            if (ecc < 1) {
                distance = semiMajorAxis * (1.0d - ecc * Math.Cos(eccentricAnomaly));
            } else if (ecc > 1) {
                distance = semiMajorAxis * (ecc * Math.Cosh(eccentricAnomaly) - 1.0d);
            }

            Angle trueAnomaly;
            if (ecc < 1) {
                // tan(v/2) = ((1 + e)/(1 - e))^(1/2) * tan(E/2)
                var term1 = Math.Sqrt((1d + ecc) / (1d - ecc)) * Math.Tan(eccentricAnomaly / 2d);
                trueAnomaly = NormalizeRadians(2d * Math.Atan(term1));
            } else {
                // tan(v/2) = ((e + 1)/(e - 1))^(1/2) * tanh(E/2)
                var term1 = Math.Sqrt((ecc + 1d) / (ecc - 1d)) * Math.Tanh(eccentricAnomaly / 2d);
                trueAnomaly = NormalizeRadians(2d * Math.Atan(term1));
            }

            var eclipticAngle = (trueAnomaly + orbital.ArgumentOfPerihelion).Radians;
            var longitudeOfAscendingNode = orbital.LongitudeOfAscendingNode.Radians;
            var orbitalInclination = orbital.Inclination.Radians;
            return new RectangularCoordinates(
                Epoch.J2000,
                distance * (Math.Cos(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) - Math.Sin(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) * Math.Cos(orbitalInclination)),
                distance * (Math.Cos(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) + Math.Sin(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) * Math.Cos(orbitalInclination)),
                distance * Math.Sin(eclipticAngle) * Math.Sin(orbitalInclination));
        }

        private static void kepler_original(JPLOsculatingOrbitalElements orbital, double tt1, double tt2) {
            // TODO: Osculation factors in perturbation and should be leveraged instead.
            //       REFERENCE = http://www.stargazing.net/kepler/ellipse.html

            var jdtt = tt1 + tt2;
            var ecc = orbital.e;
            // var meanAnomaly = orbital.M_deg * RAD_PER_DEG;
            var epochJd = orbital.Epoch_jd;
            var omega = orbital.w_deg; // argument of perihelion
            var perihelionDistance = orbital.q_km; // periapsis

            // r1, r2 = perihelion, aphelion
            // r1 = a(1 - e)
            // r2 = a(1 + e)
            // r1 + r2 = 2a
            var semiMajorAxis = perihelionDistance / (1 - ecc);
            var aphelionDistance = 2 * semiMajorAxis - perihelionDistance;
            if (ecc > 1) {
                semiMajorAxis *= -1;
            }

            // r = distance to body from focus
            // v = angle from perihelion to position

            // h^2 = mu * a * (1 - e^2)
            var h = Math.Sqrt(SUN_MU_KMD * semiMajorAxis * Math.Abs(1.0d - ecc * ecc));
            // n^2 * a^3 = mu
            var n2 = SUN_MU_KMD / (semiMajorAxis * semiMajorAxis * semiMajorAxis);
            var n = Math.Sqrt(n2);

            var daysSincePeriapsis = jdtt - orbital.PeriapsisTime_jd;
            // TODO: Take modulus so between -PI, PI, if bound orbit
            var meanAnomaly = n * daysSincePeriapsis; // radians

            // Newtown-Raphson to solve for the eccentric anomaly. Spherical Astronomy 6.4
            var estimate = meanAnomaly + ecc * Math.Sin(meanAnomaly);
            var tolerance = 1.0e-11;
            double estimateError = double.PositiveInfinity;

            // TODO: Add bounds to detect non-convergence
            if (ecc < 1) {
                // Solve M = E - e * sin(E), for E
                while (Math.Abs(estimateError) > tolerance) {
                    estimateError = estimate - ecc * Math.Sin(estimate) - meanAnomaly;
                    estimate -= estimateError / (1.0d - ecc * Math.Cos(estimate));
                }
            } else if (ecc > 1) {
                // Solve M = e * sinh(E) - E, for E
                while (Math.Abs(estimateError) > tolerance) {
                    estimateError = meanAnomaly + estimate - ecc * Math.Sinh(estimate);
                    estimate += estimateError / (ecc * Math.Cosh(estimate) - 1.0d);
                }
            }
            var eccentricAnomaly = estimate;

            double distance = double.NaN;
            if (ecc < 1) {
                distance = semiMajorAxis * (1.0d - ecc * Math.Cos(eccentricAnomaly));
            } else if (ecc > 1) {
                distance = semiMajorAxis * (ecc * Math.Cosh(eccentricAnomaly) - 1.0d);
            }

            var distanceAU = distance / AE_AU;
            double trueAnomaly;
            // v = true anomaly
            if (ecc < 1) {
                // tan(v/2) = ((1 + e)/(1 - e))^(1/2) * tan(E/2)
                var term1 = Math.Sqrt((1d + ecc) / (1d - ecc)) * Math.Tan(eccentricAnomaly / 2d);
                trueAnomaly = NormalizeRadians(2d * Math.Atan(term1)).Radians;
            } else {
                // tan(v/2) = ((e + 1)/(e - 1))^(1/2) * tanh(E/2)
                var term1 = Math.Sqrt((ecc + 1d) / (ecc - 1d)) * Math.Tanh(eccentricAnomaly / 2d);
                trueAnomaly = NormalizeRadians(2d * Math.Atan(term1)).Radians;
            }

            var givenTrueAnomaly = NormalizeRadians(orbital.nu_deg * RAD_PER_DEG);

            var eclipticAngle = trueAnomaly + omega;
            var longitudeOfAscendingNode = orbital.Omega_deg * RAD_PER_DEG;
            var orbitalInclination = orbital.i_deg * RAD_PER_DEG;
            var eclipticPositionVector = new double[3];
            eclipticPositionVector[0] = distance * (Math.Cos(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) - Math.Sin(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) * Math.Cos(orbitalInclination));
            eclipticPositionVector[1] = distance * (Math.Cos(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) + Math.Sin(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) * Math.Cos(orbitalInclination));
            eclipticPositionVector[2] = distance * Math.Sin(eclipticAngle) * Math.Sin(orbitalInclination);

            var j2000DateTime = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double j2000Tai1 = 0, j2000Tai2 = 0, j2000Tt1 = 0, j2000Tt2 = 0;
            var j2000Jd = AstroUtil.GetJulianDate(j2000DateTime);

            SOFA.UtcTai(j2000Jd, 0.0, ref j2000Tai1, ref j2000Tai2);
            SOFA.TaiTt(j2000Tai1, j2000Tai2, ref j2000Tt1, ref j2000Tt2);

            var equatorialPositionVector = new double[3];
            var meanObliquity = AstrometricConstants.J2000MeanObliquity.Radians;

            equatorialPositionVector[0] = eclipticPositionVector[0];
            equatorialPositionVector[1] = eclipticPositionVector[1] * Math.Cos(meanObliquity) - eclipticPositionVector[2] * Math.Sin(meanObliquity);
            equatorialPositionVector[2] = eclipticPositionVector[1] * Math.Sin(meanObliquity) + eclipticPositionVector[2] * Math.Cos(meanObliquity);

            // Convert to J2000 ecliptic - https://ssd.jpl.nasa.gov/planets/approx_pos.html
            Console.WriteLine();
        }
    }
}