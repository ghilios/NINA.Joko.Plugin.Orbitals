﻿#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Utility;
using ProtoBuf;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public static class Kepler {
        private const int MAX_ECCENTRIC_ANOMALY_ITERATIONS = 20;

        [ProtoContract(SkipConstructor = true)]
        public class GravitationalParameter {
            public static readonly GravitationalParameter Zero = Create(0.0);
            public static readonly GravitationalParameter Sun = Create(1.32712440018e20);
            public static readonly GravitationalParameter Earth = Create(3.986004418e14);

            [ProtoMember(1)]
            private readonly double parameter_m3_s2; // Units in m^3/s^2

            private GravitationalParameter(double parameter) {
                this.parameter_m3_s2 = parameter;
            }

            public double Parameter_m3_s2 => parameter_m3_s2;

            // Units in km^3 / days ^ 2
            public double Parameter_km3_d2 => parameter_m3_s2 * AstrometricConstants.M3_S2_TO_KM3_D2_FACTOR;

            // Units in au^3 / days ^ 2
            public double Parameter_au3_d2 => parameter_m3_s2 * AstrometricConstants.M3_S2_TO_AU3_D2_FACTOR;

            public static GravitationalParameter Create(double parameter_m3_s2) {
                return new GravitationalParameter(parameter_m3_s2);
            }

            public static explicit operator GravitationalParameter(double b) => Create(b);

            public override string ToString() {
                return $"{{Parameter={Parameter_m3_s2} m³/s²}}";
            }
        }

        [ProtoContract(SkipConstructor = true)]
        public class OrbitalElements {

            public OrbitalElements(string name) {
                this.Name = name;
            }

            private OrbitalElements() {
            }

            [ProtoMember(1)]
            public string Name { get; private set; }

            [ProtoMember(2)]
            public GravitationalParameter PrimaryGravitationalParameter { get; set; } = GravitationalParameter.Zero;

            [ProtoMember(3)]
            public GravitationalParameter SecondaryGravitationalParameter { get; set; } = GravitationalParameter.Zero;

            [ProtoMember(4)]
            public double Epoch_jd { get; set; } = double.NaN;

            [ProtoMember(5)]
            public double? q_Perihelion_au { get; set; }

            [ProtoMember(6)]
            public double e_Eccentricity { get; set; } = double.NaN;

            [ProtoMember(7)]
            public double i_Inclination_rad { get; set; } = double.NaN;

            [ProtoMember(8)]
            public double w_ArgOfPerihelion_rad { get; set; } = double.NaN;

            [ProtoMember(9)]
            public double node_LongitudeOfAscending_rad { get; set; } = double.NaN;

            [ProtoMember(10)]
            public double? tp_PeriapsisTime_jd { get; set; }

            [ProtoMember(11)]
            public double? M_MeanAnomalyAtEpoch { get; set; }

            [ProtoMember(12)]
            public double? a_SemiMajorAxis_au { get; set; }

            public override string ToString() {
                return $"{{{nameof(Name)}={Name}, {nameof(PrimaryGravitationalParameter)}={PrimaryGravitationalParameter}, {nameof(SecondaryGravitationalParameter)}={SecondaryGravitationalParameter}, {nameof(Epoch_jd)}={Epoch_jd.ToString()}, {nameof(q_Perihelion_au)}={q_Perihelion_au.ToString()}, {nameof(e_Eccentricity)}={e_Eccentricity.ToString()}, {nameof(i_Inclination_rad)}={i_Inclination_rad.ToString()}, {nameof(w_ArgOfPerihelion_rad)}={w_ArgOfPerihelion_rad.ToString()}, {nameof(node_LongitudeOfAscending_rad)}={node_LongitudeOfAscending_rad.ToString()}, {nameof(tp_PeriapsisTime_jd)}={tp_PeriapsisTime_jd.ToString()}, {nameof(M_MeanAnomalyAtEpoch)}={M_MeanAnomalyAtEpoch.ToString()}, {nameof(a_SemiMajorAxis_au)}={a_SemiMajorAxis_au.ToString()}}}";
            }
        }

        [ProtoContract]
        public class PVTableRow {

            [ProtoMember(1)]
            public double Epoch_jd { get; set; }

            [ProtoMember(2)]
            public double X { get; set; }

            [ProtoMember(3)]
            public double Y { get; set; }

            [ProtoMember(4)]
            public double Z { get; set; }

            [ProtoMember(5)]
            public double VelocityX { get; set; }

            [ProtoMember(6)]
            public double VelocityY { get; set; }

            [ProtoMember(7)]
            public double VelocityZ { get; set; }

            public RectangularCoordinates GetPosition() {
                return new RectangularCoordinates(X, Y, Z);
            }

            public RectangularCoordinates GetVelocity() {
                return new RectangularCoordinates(VelocityX, VelocityY, VelocityZ);
            }

            public override string ToString() {
                return $"{{{nameof(Epoch_jd)}={Epoch_jd.ToString()}, {nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(Z)}={Z.ToString()}, {nameof(VelocityX)}={VelocityX.ToString()}, {nameof(VelocityY)}={VelocityY.ToString()}, {nameof(VelocityZ)}={VelocityZ.ToString()}}}";
            }
        }

        [ProtoContract(SkipConstructor = true)]
        public class PVTable {

            public PVTable(string name) {
                this.Name = name;
            }

            [ProtoMember(1)]
            public string Name { get; private set; }

            [ProtoMember(2)]
            public List<PVTableRow> Rows { get; set; }

            public override string ToString() {
                return $"{{{nameof(Name)}={Name}, {nameof(Rows)}={Rows}}}";
            }
        }

        public class OrbitalPosition {

            public OrbitalPosition(string name, double asof_jd) {
                this.Name = name;
                this.AsOf_jd = asof_jd;
            }

            public string Name { get; private set; }
            public double AsOf_jd { get; private set; }
            public double M_MeanAnomaly_rad { get; set; } = double.NaN;
            public double e_EccentricAnomaly_rad { get; set; } = double.NaN;
            public double v0_TrueAnomaly_rad { get; set; } = double.NaN;
            public double Distance_au { get; set; } = double.NaN;
            public RectangularCoordinates EclipticCoordinates { get; set; }

            public override string ToString() {
                return $"{{{nameof(Name)}={Name}, {nameof(AsOf_jd)}={AsOf_jd.ToString()}, {nameof(M_MeanAnomaly_rad)}={M_MeanAnomaly_rad.ToString()}, {nameof(e_EccentricAnomaly_rad)}={e_EccentricAnomaly_rad.ToString()}, {nameof(v0_TrueAnomaly_rad)}={v0_TrueAnomaly_rad.ToString()}, {nameof(Distance_au)}={Distance_au.ToString()}, {nameof(EclipticCoordinates)}={EclipticCoordinates}}}";
            }
        }

        public static RectangularPV GetPVOnEarthSurface(DateTime asof, Angle latitude, Angle longitude, double elevation) {
            var jd = AstroUtil.GetJulianDate(asof);
            var deltaT = AstroUtil.DeltaT(asof);
            var observer = new NOVAS.Observer() {
                Where = 1,
                OnSurf = new NOVAS.OnSurface() {
                    Latitude = latitude.Degree,
                    Longitude = longitude.Degree,
                    Height = elevation
                }
            };

            var pos = new double[3];
            var vel = new double[3];
            var result = NOVAS.NOVAS_geo_posvel(jd, deltaT, NOVAS.Accuracy.Full, observer, pos, vel);
            if (result != 0) {
                throw new Exception($"NOVAS geo_posvel failed. Result={result}");
            }
            return new RectangularPV(
                new RectangularCoordinates(pos[0], pos[1], pos[2]),
                new RectangularCoordinates(vel[0], vel[1], vel[2]));
        }

        public static RectangularCoordinates GetApparentPosition(
            OrbitalPosition orbitalPosition,
            NOVAS.Body orbitalCenterBody,
            Angle latitude,
            Angle longitude,
            double elevation) {
            var centerPosition = NOVAS.BodyPositionAndVelocity(orbitalPosition.AsOf_jd, orbitalCenterBody, NOVAS.SolarSystemOrigin.SolarCenterOfMass);
            var asof = NOVAS.JulianToDateTime(orbitalPosition.AsOf_jd);
            var pvOnSurface = GetPVOnEarthSurface(asof, latitude, longitude, elevation);

            // NOVAS returns ecliptic coordinates. Reverse the ecliptic rotation to get to equatorial so we can subtract from the orbital position
            var earthPositionEquatorial = centerPosition.Position.RotateEcliptic(-AstrometricConstants.J2000MeanObliquity);

            var earthSurfacePosition = orbitalPosition.EclipticCoordinates - earthPositionEquatorial - pvOnSurface.Position.RotateEcliptic(-AstrometricConstants.J2000MeanObliquity);
            return earthSurfacePosition.RotateEcliptic(AstrometricConstants.J2000MeanObliquity);
        }

        public static OrbitalPosition CalculateOrbitalElements(
            OrbitalElements orbitalElements,
            double asOf_jd,
            double eccentricAnomalyTolerance = 1.0e-11) {
            var orbitalPosition = new OrbitalPosition(orbitalElements.Name, asOf_jd);
            var ecc = orbitalElements.e_Eccentricity;
            var isParabolic = Math.Abs(ecc - 1.0d) < double.Epsilon;
            var gravParam = orbitalElements.PrimaryGravitationalParameter.Parameter_au3_d2 + orbitalElements.SecondaryGravitationalParameter.Parameter_au3_d2;
            // TODO: Add validation for invalid combinations of missing parameters, and inconsistencies

            if (!isParabolic) {
                // r1, r2 = perihelion, aphelion
                // r1 = a(1 - e)
                // r2 = a(1 + e)
                // r1 + r2 = 2a
                double semiMajorAxis;
                if (!orbitalElements.a_SemiMajorAxis_au.HasValue) {
                    semiMajorAxis = orbitalElements.q_Perihelion_au.Value / (1 - ecc);
                    if (ecc > 1) {
                        semiMajorAxis *= -1;
                    }
                } else {
                    semiMajorAxis = orbitalElements.a_SemiMajorAxis_au.Value;
                }

                // r = distance to body from focus
                // v = angle from perihelion to position

                // n^2 * a^3 = mu
                var n2 = gravParam / (semiMajorAxis * semiMajorAxis * semiMajorAxis);
                var n = Math.Sqrt(n2);

                if (!orbitalElements.M_MeanAnomalyAtEpoch.HasValue) {
                    // Mean anomaly needs to be populated

                    var daysSincePeriapsis = asOf_jd - orbitalElements.tp_PeriapsisTime_jd.Value;
                    orbitalPosition.M_MeanAnomaly_rad = n * daysSincePeriapsis;
                } else {
                    var daysSinceEpoch = asOf_jd - orbitalElements.Epoch_jd;
                    orbitalPosition.M_MeanAnomaly_rad = orbitalElements.M_MeanAnomalyAtEpoch.Value + n * daysSinceEpoch;
                }

                var meanAnomaly = orbitalPosition.M_MeanAnomaly_rad;

                var estimate = meanAnomaly + ecc * Math.Sin(meanAnomaly);
                double estimateError = double.PositiveInfinity;

                int iterations = 0;
                if (ecc < 1) {
                    // Solve M = E - e * sin(E), for E
                    while (Math.Abs(estimateError) > eccentricAnomalyTolerance && iterations++ < MAX_ECCENTRIC_ANOMALY_ITERATIONS) {
                        estimateError = estimate - ecc * Math.Sin(estimate) - meanAnomaly;
                        estimate -= estimateError / (1.0d - ecc * Math.Cos(estimate));
                    }
                } else {
                    // Solve M = e * sinh(E) - E, for E
                    while (Math.Abs(estimateError) > eccentricAnomalyTolerance && iterations++ < MAX_ECCENTRIC_ANOMALY_ITERATIONS) {
                        estimateError = meanAnomaly + estimate - ecc * Math.Sinh(estimate);
                        estimate += estimateError / (ecc * Math.Cosh(estimate) - 1.0d);
                    }
                }

                if (iterations >= MAX_ECCENTRIC_ANOMALY_ITERATIONS) {
                    throw new Exception($"Maximum ({MAX_ECCENTRIC_ANOMALY_ITERATIONS}) iterations exceeded while calculating eccentric anomaly for {orbitalElements.Name}");
                }
                orbitalPosition.e_EccentricAnomaly_rad = estimate;

                if (ecc < 1) {
                    orbitalPosition.Distance_au = semiMajorAxis * (1.0d - ecc * Math.Cos(orbitalPosition.e_EccentricAnomaly_rad));

                    // tan(v/2) = ((1 + e)/(1 - e))^(1/2) * tan(E/2)
                    var term1 = Math.Sqrt((1d + ecc) / (1d - ecc)) * Math.Tan(orbitalPosition.e_EccentricAnomaly_rad / 2d);
                    orbitalPosition.v0_TrueAnomaly_rad = AstrometricConstants.NormalizeRadians(2d * Math.Atan(term1));
                } else {
                    orbitalPosition.Distance_au = semiMajorAxis * (ecc * Math.Cosh(orbitalPosition.e_EccentricAnomaly_rad) - 1.0d);

                    // tan(v/2) = ((e + 1)/(e - 1))^(1/2) * tanh(E/2)
                    var term1 = Math.Sqrt((ecc + 1d) / (ecc - 1d)) * Math.Tanh(orbitalPosition.e_EccentricAnomaly_rad / 2d);
                    orbitalPosition.v0_TrueAnomaly_rad = AstrometricConstants.NormalizeRadians(2d * Math.Atan(term1));
                }
            } else {
                // Parabolic orbit. Use Barker's equation
                //  1        v           v      mu    1
                //  _ * (tan(_))^3 + tan(_) = (____)^(_) * (t - t0)
                //  3        2           2     2q^3   2
                // See https://adsabs.harvard.edu/full/1985JBAA...95..113M for an elegant algebraic solution

                // Units are
                //  Time: days
                //  Distance: au
                //  Mass: kg
                var mu = gravParam;
                var q = orbitalElements.q_Perihelion_au.Value;
                var q3 = q * q * q;
                var daysSincePeriapsis = asOf_jd - orbitalElements.tp_PeriapsisTime_jd.Value;
                var W = (3.0 / 2.0) * Math.Sqrt(mu / (2.0 * q3)) * daysSincePeriapsis;
                var underRadical = W - Math.Sqrt(W * W + 1);
                var y = Math.Pow(Math.Abs(underRadical), 1.0 / 3.0);
                if (underRadical < 0) {
                    y = -y;
                }
                var x = y - 1.0 / y;
                var v = Math.Atan(x) * 2.0;

                orbitalPosition.v0_TrueAnomaly_rad = v;

                // See https://www.bogan.ca/orbits/kepler/orbteqtn.html for a summary of equations about orbits
                // r = distance from central body
                // q = periapsis distance
                // T = theta = true anomaly
                //
                // r = 2q / (1 + cos(T))
                var r = 2.0 * q / (1.0 + Math.Cos(v));
                orbitalPosition.Distance_au = r;
            }

            var eclipticAngle = orbitalPosition.v0_TrueAnomaly_rad + orbitalElements.w_ArgOfPerihelion_rad;
            var longitudeOfAscendingNode = orbitalElements.node_LongitudeOfAscending_rad;
            var orbitalInclination = orbitalElements.i_Inclination_rad;
            orbitalPosition.EclipticCoordinates = new RectangularCoordinates(
                orbitalPosition.Distance_au * (Math.Cos(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) - Math.Sin(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) * Math.Cos(orbitalInclination)),
                orbitalPosition.Distance_au * (Math.Cos(eclipticAngle) * Math.Sin(longitudeOfAscendingNode) + Math.Sin(eclipticAngle) * Math.Cos(longitudeOfAscendingNode) * Math.Cos(orbitalInclination)),
                orbitalPosition.Distance_au * Math.Sin(eclipticAngle) * Math.Sin(orbitalInclination));
            return orbitalPosition;
        }

        public static Coordinates SunOrbitalPositionToICRF(OrbitalPosition orbitalPosition) {
            return orbitalPosition.EclipticCoordinates.RotateEcliptic(AstrometricConstants.J2000MeanObliquity).ToPolar();
        }
    }
}