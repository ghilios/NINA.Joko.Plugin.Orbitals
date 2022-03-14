﻿using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestApp {

    public class RectangularPV {
        public RectangularPV(RectangularCoordinates position, RectangularCoordinates velocity) {
            this.Position = position;
            this.Velocity = velocity;
        }
        
        public RectangularCoordinates Position { get; private set; }
        public RectangularCoordinates Velocity { get; private set; }

        public override string ToString() {
            return $"{{{nameof(Position)}={Position}, {nameof(Velocity)}={Velocity}}}";
        }
    }

    public class RectangularCoordinates {

        public RectangularCoordinates(double x, double y, double z) {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Z { get; private set; }
        public double Distance => Math.Sqrt(X * X + Y * Y + Z * Z);

        public RectangularCoordinates RotateEcliptic(Angle meanObliquity) {
            var meanObliquityRad = meanObliquity.Radians;
            var x = this.X;
            var y = this.Y * Math.Cos(meanObliquityRad) - this.Z * Math.Sin(meanObliquityRad);
            var z = this.Y * Math.Sin(meanObliquityRad) + this.Z * Math.Cos(meanObliquityRad);
            return new RectangularCoordinates(x, y, z);
        }

        public static RectangularCoordinates operator -(RectangularCoordinates l, RectangularCoordinates r) {
            return new RectangularCoordinates(
                l.X - r.X,
                l.Y - r.Y,
                l.Z - r.Z);
        }

        public static RectangularCoordinates operator *(RectangularCoordinates l, double mult) {
            return new RectangularCoordinates(
                l.X * mult,
                l.Y * mult,
                l.Z * mult);
        }

        public static RectangularCoordinates operator /(RectangularCoordinates l, double div) {
            return new RectangularCoordinates(
                l.X / div,
                l.Y / div,
                l.Z / div);
        }

        public Coordinates ToPolar() {
            var ra = Angle.ByRadians(Math.Atan2(this.Y, this.X));
            var dec = Angle.ByRadians(Math.Asin(this.Z / this.Distance));
            return new Coordinates(ra: ra, dec: dec, epoch: Epoch.J2000);
        }

        public override string ToString() {
            return $"{{{nameof(X)}={X.ToString()}, {nameof(Y)}={Y.ToString()}, {nameof(Z)}={Z.ToString()}}}";
        }
    }
}
