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
using System.Linq;
using System.Threading.Tasks;
using static TestApp.Kepler;

namespace TestApp {
    internal class Program {

        private static async Task Main(string[] args) {
            var accessor = new JPLAccessor();
            var lmd = await accessor.GetCometElementsLastModified();
            var response = await accessor.GetCometElements();
            response.ParseError += (sender, e) => {
                Console.WriteLine($"Error parsing comets: {e.ErrorMessage}");
            };

            var cometsByName = response.Response.ToDictionary(r => r.name, r => r);
            var comet = cometsByName["C/2021 O3 (PANSTARRS)"];
            var cometOrbitalElement = comet.ToOrbitalElements();
            var now = DateTime.UtcNow;
            var nowJd = AstroUtil.GetJulianDate(now);
            var cometOrbitalPosition = Kepler.CalculateOrbitalElements(cometOrbitalElement, nowJd);

            var earthPosition = NOVASEx.SolarSystemBodyPV(nowJd, NOVASEx.SolarSystemBody.Earth, NOVASEx.SolarSystemOrigin.SolarCenterOfMass);
            // NOVAS returns ecliptic coordinates. Reverse the ecliptic rotation to get to equatorial so we can subtract from the orbital position
            var earthPositionEquatorial = earthPosition.Position.RotateEcliptic(Angle.ByRadians(-SOFAEx.J2000MeanObliquity.Radians));
            
            var earthCenteredPosition = cometOrbitalPosition.EclipticCoordinates - earthPositionEquatorial;
            earthCenteredPosition = earthCenteredPosition.RotateEcliptic(SOFAEx.J2000MeanObliquity);
            var cometCoordinates = earthCenteredPosition.ToPolar();
            var cometCoordinatesJNow = cometCoordinates.Transform(Epoch.JNOW);
            var marsPosition = NOVASEx.GetApparentCoordinates(nowJd, NOVASEx.SolarSystemBody.Mars);

            var tenSeconds = 1.0 / AstrometricConstants.SEC_PER_DAY;
            var marsPosition2 = NOVASEx.GetApparentCoordinates(nowJd + tenSeconds, NOVASEx.SolarSystemBody.Mars);
            var raPerSec = AstroUtil.DegreeToArcsec((marsPosition2.RADegrees - marsPosition.RADegrees));
            var decPerSec = AstroUtil.DegreeToArcsec((marsPosition2.Dec - marsPosition.Dec));

            Console.WriteLine(earthPosition);
        }
    }
}