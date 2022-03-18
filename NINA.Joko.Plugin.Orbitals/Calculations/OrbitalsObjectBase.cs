#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using OxyPlot;
using OxyPlot.Axes;
using System;
using System.Linq;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public abstract class OrbitalsObjectBase : SkyObjectBase {

        public OrbitalsObjectBase(
            string name,
            CustomHorizon customHorizon) : base(name, string.Empty, customHorizon) {
        }

        public abstract MoonInfo Moon { get; protected set; }

        public override Coordinates Coordinates {
            get => CoordinatesAt(DateTime.Now);
            set {
                // No-op
            }
        }

        public override SiderealShiftTrackingRate ShiftTrackingRate {
            get => ShiftTrackingRateAt(DateTime.Now);
        }

        public override Coordinates CoordinatesAt(DateTime at) {
            var pv = GetObjectPosition(at);
            return pv.Coordinates;
        }

        public override SiderealShiftTrackingRate ShiftTrackingRateAt(DateTime at) {
            var pv = GetObjectPosition(at);
            return pv.TrackingRate;
        }

        public RectangularCoordinates Position {
            get => PositionAt(DateTime.Now);
        }

        public RectangularCoordinates PositionAt(DateTime at) {
            var pv = GetObjectPosition(at);
            return pv.Position;
        }

        private DateTime lastObjectCalculationDateTime = DateTime.MinValue;
        private OrbitalPositionVelocity lastObjectCalculation = null;

        private OrbitalPositionVelocity GetObjectPosition(DateTime at) {
            var timeSinceCalculation = at - lastObjectCalculationDateTime;
            if (lastObjectCalculation != null && Math.Abs(timeSinceCalculation.TotalSeconds) < 1.0) {
                return lastObjectCalculation;
            }

            var pv = CalculateObjectPosition(at);
            this.lastObjectCalculation = pv;
            this.lastObjectCalculationDateTime = at;
            return pv;
        }

        protected abstract OrbitalPositionVelocity CalculateObjectPosition(DateTime at);

        protected override void UpdateHorizonAndTransit() {
            // It turns out that 2/3 of calls made here are during clone operations and deserialization,
            // at which time the Coordinates object is basically "empty" (RA = 0, Dec = 0, Epoch = J2000)
            // Each call to this method generates up to 1,000 or more calls to AstroUtil

            // Basically, each DSO in a template or sequence or target will call here 20 or so times, each
            // time looping 240 times getting altitude/azimuth, etc.  Only a couple of those 20 are worthwhile.

            // For 100 DSO's (not unreasonable at all), that's 2000 * 240 * 3 calls to AstroUtils, taking up
            // many actual seconds of work (which prevents the display of the target list, among other things)
            // That's over one million calls!

            // 80%+ of the remaining calls (or more) could be removed if deserialization didn't generate
            // a call here 8 or more times for each Coordinates object (RA hours, minutes, seconds; Dec hours, minutes, seconds;
            // rotation; and more)!  It's unclear how to do that, so I leave it to others

            if (Coordinates == null || (Coordinates.RA == 0 && Coordinates.Dec == 0 && Coordinates.Epoch == Epoch.J2000) || double.IsNaN(Coordinates.Dec)) {
                return;
            }

            var start = _referenceDate;

            // Do this every time in case reference date has changed
            Moon.SetReferenceDateAndObserver(_referenceDate, new ObserverInfo { Latitude = _latitude, Longitude = _longitude });

            Altitudes.Clear();
            Horizon.Clear();

            for (double hourDelta = 0; hourDelta < 24; hourDelta += 0.1) {
                var coordinates = CoordinatesAt(start);
                var siderealTime = AstroUtil.GetLocalSiderealTime(start, _longitude);
                var hourAngle = AstroUtil.GetHourAngle(siderealTime, coordinates.RA);
                var degAngle = AstroUtil.HoursToDegrees(hourAngle);
                var altitude = AstroUtil.GetAltitude(degAngle, _latitude, coordinates.Dec);
                var azimuth = AstroUtil.GetAzimuth(degAngle, altitude, _latitude, coordinates.Dec);

                Altitudes.Add(new DataPoint(DateTimeAxis.ToDouble(start), altitude));

                if (customHorizon != null) {
                    var horizonAltitude = customHorizon.GetAltitude(azimuth);
                    Horizon.Add(new DataPoint(DateTimeAxis.ToDouble(start), horizonAltitude));
                }

                start = start.AddHours(0.1);
            }

            MaxAltitude = Altitudes.OrderByDescending((x) => x.Y).FirstOrDefault();

            CalculateTransit(_latitude);
        }

        private void CalculateTransit(double latitude) {
            // For simplification, use the coordinates at the reference time to determine whether a south transit happens. This assumes the
            // orbital object doesn't move at a very high velocity. Satellites are likely the only objects that fit this, and we don't support
            // them anyways
            var coordinates = Coordinates;
            var alt0 = AstroUtil.GetAltitude(0, latitude, coordinates.Dec);
            var alt180 = AstroUtil.GetAltitude(180, latitude, coordinates.Dec);
            double transit;
            if (alt0 > alt180) {
                transit = AstroUtil.GetAzimuth(0, alt0, latitude, coordinates.Dec);
            } else {
                transit = AstroUtil.GetAzimuth(180, alt180, latitude, coordinates.Dec);
            }
            DoesTransitSouth = !double.IsNaN(transit) && Convert.ToInt32(transit) == 180;
        }
    }
}