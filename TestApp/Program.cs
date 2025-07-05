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
using NINA.Joko.Plugin.Orbitals.Utility;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace TestApp {

    internal class Program {

        private static async Task Main(string[] args) {
            var accessor = new JPLAccessor();
            var comets = await accessor.GetCometElements(CancellationToken.None);
            var now = DateTime.UtcNow;
            var nowJd = AstroUtil.GetJulianDate(now);
            var latitude = Angle.ByDegree(41.292198d);
            var longitude = Angle.ByDegree(-74.361229d);
            double elevation = 300d;
            using (var ms = new MemoryStream()) {
                foreach (var cometElements in comets.Response) {
                    var cometNameLower = cometElements.name.ToLowerInvariant();
                    //var target = "c/2019 q4 (borisov)";
                    //var target = "c/1962 c1 (seki-lines)";
                    //if (cometNameLower != target) {
                    //    continue;
                    //}

                    Console.WriteLine($"Solving for {cometNameLower}");
                    var orbitalElements = cometElements.ToOrbitalElements();
                    var orbitalPosition = Kepler.CalculateOrbitalElements(orbitalElements, nowJd);
                    var orbitalApparentPosition = Kepler.GetApparentPosition(orbitalPosition, NOVAS.Body.Earth, latitude, longitude, elevation);
                    var orbitalCoordinates = orbitalApparentPosition.ToPolar();
                }
            }

            /*
            var accessor = new JPLAccessor();
            var lmd = await accessor.GetNumberedAsteroidsLastModified();
            var response = await accessor.GetNumberedAsteroidElements();
            response.ParseError += (sender, e) => {
                Console.WriteLine($"Error parsing comets: {e.ErrorMessage}");
            };
            var now = DateTime.UtcNow;
            var nowJd = AstroUtil.GetJulianDate(now);

            var cometsByName = new SuffixTrie<JPLOrbitalElements>(3);
            using (var ms = new MemoryStream()) {
                foreach (var cometElements in response.Response) {
                    var cometNameLower = cometElements.name.ToLowerInvariant();
                    cometsByName.Add(cometNameLower, cometElements);

                    var orbitalElements = cometElements.ToOrbitalElements();
                    var orbitalPosition = Kepler.CalculateOrbitalElements(orbitalElements, nowJd);
                    var orbitalApparentPosition = Kepler.GetApparentPosition(orbitalPosition, NOVAS.Body.Earth);
                    var orbitalCoordinates = orbitalApparentPosition.ToPolar();
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
            */

            /*
            var date = DateTime.Now;
            var jd = AstroUtil.GetJulianDate(date);
            long jd_high = (long)jd;
            double jd_low = jd - jd_high;
            var deltaT = AstroUtil.DeltaT(date);

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
            var result2 = NOVASEx.NOVAS_geo_posvel(jd, deltaT, NOVAS.Accuracy.Full, observer, pos, vel);

            var posKm = pos.Select(p => p * AstrometricConstants.KM_PER_AU).ToArray();
            var posKmVector = new RectangularCoordinates(posKm[0], posKm[1], posKm[2]);
            Console.WriteLine(posKmVector);
            */

            /*
            using (var lookup = new TrigramStringMap<OrbitalElements>("comets2")) {
                var accessor = new JPLAccessor();
                var response = await accessor.GetCometElements();
                response.ParseError += (sender, e) => {
                    Console.WriteLine($"Error parsing comets: {e.ErrorMessage}");
                };
                var now = DateTime.UtcNow;
                var nowJd = AstroUtil.GetJulianDate(now);
                lookup.AddRange(k => k.Name, response.Response.Select(r => r.ToOrbitalElements()));

                var matchingStrings = lookup.Query("C/202", 20);
                var singleMatch = lookup.Lookup("ceres");
                Console.WriteLine();
            }
            */

            /*
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            var path = @"C:\Users\ghili\AppData\Local\NINA\OrbitalElements\NumberedAsteroidsElements.bin.gz";
            using (var lookup = new TrigramStringMap<OrbitalElements>()) {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gs = new GZipStream(fs, CompressionMode.Decompress)) {
                    var orbitalElements = ProtoBuf.Serializer.DeserializeItems<OrbitalElements>(gs, ProtoBuf.PrefixStyle.Base128, 1);
                    lookup.AddRange((o) => o.Name, orbitalElements);
                }
            }
            stopWatch.Stop();
            Console.WriteLine($"Elapsed: {stopWatch.Elapsed}");
            */

            /*
            var jplAccessor = new JPLAccessor();
            var startTime = new DateTime(2022, 3, 25, 12, 0, 0, DateTimeKind.Utc);
            var jwstTable = await jplAccessor.GetJWSTVectorTable(startTime - TimeSpan.FromHours(1), TimeSpan.FromDays(1));
            var vectorTable = jwstTable.ToPVTable();
            var orbitalElementsAccessor = new OrbitalElementsAccessor();
            var interval = TimeSpan.FromMinutes(5);
            var queryTime = startTime;

            for (int i = 0; i < 48; ++i) {
                var orbitalPV = orbitalElementsAccessor.GetPVFromTable(queryTime, vectorTable, latitude, longitude, elevation);
                Console.WriteLine($"{AstroUtil.GetJulianDate(queryTime)} -> {orbitalPV.Coordinates}");
                queryTime += interval;
            }
            */

            /*
            String example = "0062P         2023 12 25.1079  1.264990  0.624678   47.2939   68.6709    4.7380  20231223   8.0 10.0  62P/Tsuchinshan                                          MPEC 2023-Y70";
            using (var sr = new StreamReader(GenerateStreamFromString(example))) {
                var mpcAccessor = new MPCCometResponse(sr);
                var results = mpcAccessor.Response;
            }
            */

            /*
            var cometName = "C/2024 C4 (ATLAS)";
            var mpcAccessor = new MPCAccessor();
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var lastModified = await mpcAccessor.GetCometElementsLastModified(timeoutCts.Token);
            var cometElements = await mpcAccessor.GetCometElements(timeoutCts.Token);
            var allElements = cometElements.Response.Select(r => r.ToOrbitalElements()).ToList();
            var comet = allElements.Find(oe => oe.Name.StartsWith(cometName));

            var jplAccessor = new JPLAccessor();
            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var jplLastModified = await jplAccessor.GetCometElementsLastModified(timeoutCts.Token);

            var jplCometElements = await jplAccessor.GetCometElements(timeoutCts.Token);
            var jplAllElements = jplCometElements.Response.Select(r => r.ToOrbitalElements()).ToList();
            var jplComet = jplAllElements.Find(oe => oe.Name.StartsWith(cometName));

            var asOfJd = AstroUtil.GetJulianDate(DateTime.Now);
            //var asOfJd = 2460433.5d;
            var mpcCometEpoch = NOVAS.JulianToDateTime(comet.Epoch_jd);
            var jplCometEpoch = NOVAS.JulianToDateTime(jplComet.Epoch_jd);

            // var mpcOrbitalElements = Kepler.CalculateOrbitalElements(jplComet, jplComet.Epoch_jd);
            //var jplOrbitalElements = Kepler.CalculateOrbitalElements(jplComet, asOfJd);
            var mpcOrbitalPosition = Kepler.CalculateOrbitalElements(jplComet, asOfJd);
            var orbitalApparentPosition = Kepler.GetApparentPosition(mpcOrbitalPosition, NOVAS.Body.Earth, latitude, longitude, elevation);
            var orbitalCoordinates = orbitalApparentPosition.ToPolar();
            */

            /*
            var tleString1 = "1 25544U 98067A   25108.16196759  .00021659  00000+0  39076-3 0  9990";
            var tleString2 = "2 25544  51.6381 244.3378 0005375  52.1060 306.6862 15.49672979505843";

            var tle = new Tle(tleString1, tleString2);

            var sat = new Satellite(tle);
            var location = new GeodeticCoordinate(
                SGPdotNET.Util.Angle.FromRadians(latitude.Radians),
                SGPdotNET.Util.Angle.FromRadians(longitude.Radians),
                0);
            var groundStation = new GroundStation(location);

            while (true) {
                var now = DateTime.UtcNow;
                var observation = groundStation.Observe(sat, now);
                var tc1 = new TopocentricCoordinates(
                    Angle.ByRadians(observation.Azimuth.Radians),
                    Angle.ByRadians(observation.Elevation.Radians),
                    latitude,
                    longitude);
                var eq1 = tc1.Transform(Epoch.JNOW);

            Console.WriteLine();
            */
        }

        public static Stream GenerateStreamFromString(string s) {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}