#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Gma.DataStructures.StringSearch;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TestApp {

    internal class Program {

        private static async Task Main(string[] args) {
            var accessor = new JPLAccessor();
            var lmd = await accessor.GetCometElementsLastModified();
            var response = await accessor.GetCometElements();
            response.ParseError += (sender, e) => {
                Console.WriteLine($"Error parsing comets: {e.ErrorMessage}");
            };
            var now = DateTime.UtcNow;
            var nowJd = AstroUtil.GetJulianDate(now);

            var parabolicComets = new List<Kepler.OrbitalElements>();
            var cometsByName = new SuffixTrie<JPLCometElements>(3);
            using (var ms = new MemoryStream()) {
                foreach (var cometElements in response.Response) {
                    var cometNameLower = cometElements.name.ToLowerInvariant();
                    cometsByName.Add(cometNameLower, cometElements);

                    var orbitalElements = cometElements.ToOrbitalElements();
                    if (orbitalElements.e_Eccentricity == 1.0) {
                        // Parabolic Orbit
                        parabolicComets.Add(orbitalElements);
                        var orbitalPosition = Kepler.CalculateOrbitalElements(orbitalElements, nowJd);
                        var apparentPosition = Kepler.GetApparentPosition(orbitalPosition, NOVAS.Body.Earth);
                        var coordinates = apparentPosition.ToPolar();
                        Console.WriteLine();
                    }

                    ProtoBuf.Serializer.SerializeWithLengthPrefix(ms, orbitalElements, ProtoBuf.PrefixStyle.Base128, 1);
                }

                ms.Position = 0;
                var allElements = ProtoBuf.Serializer.DeserializeItems<Kepler.OrbitalElements>(ms, ProtoBuf.PrefixStyle.Base128, 1).ToList();
                Console.WriteLine();
            }

            var searchFor = "(PANSTARRS)".ToLowerInvariant();
            // var comet = cometsByName.ValueBy("C/2021 O3 (PANSTARRS)");
            var comet = cometsByName.Retrieve(searchFor).ToList();
            var cometOrbitalElement = comet.First().ToOrbitalElements();
            var cometOrbitalPosition = Kepler.CalculateOrbitalElements(cometOrbitalElement, nowJd);

            var earthPosition = NOVAS.BodyPositionAndVelocity(nowJd, NOVAS.Body.Earth, NOVAS.SolarSystemOrigin.SolarCenterOfMass);
            // NOVAS returns ecliptic coordinates. Reverse the ecliptic rotation to get to equatorial so we can subtract from the orbital position
            var earthPositionEquatorial = earthPosition.Position.RotateEcliptic(-AstrometricConstants.J2000MeanObliquity);

            var earthCenteredPosition = cometOrbitalPosition.EclipticCoordinates - earthPositionEquatorial;
            earthCenteredPosition = earthCenteredPosition.RotateEcliptic(AstrometricConstants.J2000MeanObliquity);
            var cometCoordinates = earthCenteredPosition.ToPolar();
            var cometCoordinatesJNow = cometCoordinates.Transform(Epoch.JNOW);
            var marsPosition = NOVAS.PlanetApparentCoordinates(nowJd, NOVAS.Body.Mars);

            var tenSeconds = 1.0 / AstrometricConstants.SEC_PER_DAY;
            var marsPosition2 = NOVAS.PlanetApparentCoordinates(nowJd + tenSeconds, NOVAS.Body.Mars);
            var raPerSec = AstroUtil.DegreeToArcsec((marsPosition2.RADegrees - marsPosition.RADegrees));
            var decPerSec = AstroUtil.DegreeToArcsec((marsPosition2.Dec - marsPosition.Dec));

            Console.WriteLine(earthPosition);
        }
    }
}