#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Astrometry;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public enum MPCCometDesignation {
        MinorPlanet, // A
        UncertainObject, // X
        InterstellarObject, // I
        LongPeriod, // P
        ShortPeriod, // C
        Defunct // D
    }

    public class MPCCometElements : IOrbitalElementsSource {
        public int? number { get; set; }
        public MPCCometDesignation designation { get; set; }
        public string provisionalDesignation { get; set; }
        public int tpYear { get; set; }
        public int tpMonth { get; set; }
        public double tpDay_tt { get; set; }
        public double perihelionDistance_au { get; set; }
        public double eccentricity { get; set; }
        public double argOfPerihelion_deg { get; set; }
        public double longOfAscendingNode_deg { get; set; }
        public double incAscendingNode_deg { get; set; }
        public DateTime? epoch { get; set; }
        public double? absoluteMagnitude { get; set; }
        public double? slope { get; set; }
        public string name { get; set; }
        public string reference { get; set; }
        public string Name => name;

        public OrbitalElements ToOrbitalElements() {
            var epochJd = epoch.HasValue ? AstroUtil.GetJulianDate(epoch.Value) : double.NaN;
            var tpDay = (int)tpDay_tt;
            var tpDayPart = tpDay_tt - tpDay;
            return new OrbitalElements(name) {
                PrimaryGravitationalParameter = GravitationalParameter.Sun,
                SecondaryGravitationalParameter = GravitationalParameter.Zero,
                Epoch_jd = epochJd,
                q_Perihelion_au = perihelionDistance_au,
                e_Eccentricity = eccentricity,
                i_Inclination_rad = incAscendingNode_deg * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = argOfPerihelion_deg * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = longOfAscendingNode_deg * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = NOVAS.JulianDate((short)tpYear, (short)tpMonth, (short)tpDay, tpDayPart * 24)
            };
        }
    }

    public class MPCParseErrorDetail {
        public int RecordNumber { get; set; }
        public string ColumnName { get; set; }
        public string ErrorValue { get; set; }
        public string ErrorMessage { get; set; }
    }

    public abstract class MPCRecordResponse<T> : IDisposable {
        private readonly List<IDisposable> disposeOnCompletion;

        public MPCRecordResponse(List<IDisposable> disposeOnCompletion = null) {
            this.disposeOnCompletion = disposeOnCompletion ?? new List<IDisposable>();
        }

        public IEnumerable<T> Response { get; protected set; }

        protected bool disposed;

        protected virtual void OnDispose() {
        }

        public void Dispose() {
            if (!disposed) {
                disposeOnCompletion.ForEach(c => c.Dispose());
                OnDispose();
                disposed = true;
            }
        }
    }

    public class MPCCometResponse : MPCRecordResponse<MPCCometElements> {

        private string[] COLUMN_NAMES = {
            "Periodic Comet Number",
            "Orbit Type",
            "Provisional Designation",
            "Year of Perihelion Passage",
            "Month of Perihelion Passage",
            "Day of Perihelion Passage (TT)",
            "Perihelion Distance (AU)",
            "Orbital Eccentricity",
            "Argument of Perihelion (Deg)",
            "Longitude of Ascending Node (Deg)",
            "Inclination (Deg)",
            "Epoch of Last Perturbation",
            "Absolute Magnitude",
            "Slope",
            "Designation and Name",
            "Reference",
        };

        public MPCCometResponse(StreamReader streamReader, List<IDisposable> disposeOnCompletion = null) {
            List<MPCCometElements> response = new List<MPCCometElements>();
            try {
                int recordNumber = 0;
                int streamPosition = 0;
                int nextStreamPosition = 0;
                while (true) {
                    streamPosition = nextStreamPosition;
                    var nextLine = streamReader.ReadLine();
                    if (nextLine == null || nextLine.Trim().Length == 0) {
                        break;
                    }

                    nextStreamPosition = streamPosition + nextLine.Length + 1;
                    int columnNumber = 0;
                    string columnPortion = "";
                    try {
                        int rowIndex = 0;
                        var periodicCometNumber = ParseIntOptional(nextLine, 4, ref rowIndex, ref columnNumber, ref columnPortion);
                        var orbitType = ParseString(nextLine, 1, ref rowIndex, ref columnNumber, ref columnPortion);
                        var provisionalDesignation = ParseString(nextLine, 9, ref rowIndex, ref columnNumber, ref columnPortion);
                        var yearOfPerihelionPassage = ParseInt(nextLine, 5, ref rowIndex, ref columnNumber, ref columnPortion);
                        var monthOfPerihelionPassage = ParseInt(nextLine, 3, ref rowIndex, ref columnNumber, ref columnPortion);
                        var dayOfPerihelionPassageTT = ParseDouble(nextLine, 8, ref rowIndex, ref columnNumber, ref columnPortion);
                        var perihelionDistanceAU = ParseDouble(nextLine, 11, ref rowIndex, ref columnNumber, ref columnPortion);
                        var eccentricity = ParseDouble(nextLine, 10, ref rowIndex, ref columnNumber, ref columnPortion);
                        var argOfPerihelionDeg = ParseDouble(nextLine, 10, ref rowIndex, ref columnNumber, ref columnPortion);
                        var longitudeOfNodeDeg = ParseDouble(nextLine, 10, ref rowIndex, ref columnNumber, ref columnPortion);
                        var inclinationDeg = ParseDouble(nextLine, 10, ref rowIndex, ref columnNumber, ref columnPortion);
                        var epochString = ParseString(nextLine, 8, ref rowIndex, ref columnNumber, ref columnPortion);
                        DateTime? epoch = null;
                        if (epochString.Length != 0) {
                            epoch = DateTime.ParseExact(epochString, "yyyyMMdd", CultureInfo.InvariantCulture);
                        }
                        rowIndex += 2;
                        var absoluteMagnitude = ParseDoubleOptional(nextLine, 5, ref rowIndex, ref columnNumber, ref columnPortion);
                        var slope = ParseDoubleOptional(nextLine, 6, ref rowIndex, ref columnNumber, ref columnPortion);
                        var designationAndName = ParseString(nextLine, 57, ref rowIndex, ref columnNumber, ref columnPortion);
                        var reference = ParseString(nextLine, -1, ref rowIndex, ref columnNumber, ref columnPortion);
                        var element = new MPCCometElements {
                            number = periodicCometNumber,
                            designation = ToCometDesignation(orbitType),
                            provisionalDesignation = provisionalDesignation,
                            tpYear = yearOfPerihelionPassage,
                            tpMonth = monthOfPerihelionPassage,
                            tpDay_tt = dayOfPerihelionPassageTT,
                            perihelionDistance_au = perihelionDistanceAU,
                            argOfPerihelion_deg = argOfPerihelionDeg,
                            longOfAscendingNode_deg = longitudeOfNodeDeg,
                            incAscendingNode_deg = inclinationDeg,
                            epoch = epoch,
                            absoluteMagnitude = absoluteMagnitude,
                            slope = slope,
                            name = designationAndName,
                            reference = reference
                        };
                        response.Add(element);
                        recordNumber += 1;
                    } catch (Exception e) {
                        var detail = new MPCParseErrorDetail() {
                            RecordNumber = recordNumber,
                            ColumnName = COLUMN_NAMES[columnNumber],
                            ErrorValue = columnPortion,
                            ErrorMessage = e.Message
                        };
                        ParseError?.Invoke(this, detail);
                        throw;
                    }
                }

                this.Response = response;
            } catch (Exception) {
                streamReader?.Dispose();
                if (disposeOnCompletion != null) {
                    disposeOnCompletion.ForEach(c => c.Dispose());
                }
                throw;
            }
        }

        private static MPCCometDesignation ToCometDesignation(string str) {
            if (str == "P") {
                return MPCCometDesignation.LongPeriod;
            } else if (str == "C") {
                return MPCCometDesignation.ShortPeriod;
            } else if (str == "D") {
                return MPCCometDesignation.Defunct;
            } else if (str == "A") {
                return MPCCometDesignation.MinorPlanet;
            } else if (str == "X") {
                return MPCCometDesignation.UncertainObject;
            } else if (str == "I") {
                return MPCCometDesignation.InterstellarObject;
            } else {
                throw new ArgumentException($"{str} is not a valid comet designation", "MPCCometDesignation");
            }
        }

        private static int? ParseIntOptional(String row, int length, ref int rowIndex, ref int columnNumber, ref string columnPortion) {
            columnPortion = row.Substring(rowIndex, length);
            rowIndex += length;
            columnNumber += 1;
            var columnValue = columnPortion.Trim();
            if (columnValue.Length == 0) {
                return null;
            }
            return int.Parse(columnValue);
        }

        private static int ParseInt(String row, int length, ref int rowIndex, ref int columnNumber, ref string columnPortion) {
            columnPortion = row.Substring(rowIndex, length);
            rowIndex += length;
            columnNumber += 1;
            return int.Parse(columnPortion.Trim());
        }

        private static double? ParseDoubleOptional(String row, int length, ref int rowIndex, ref int columnNumber, ref string columnPortion) {
            columnPortion = row.Substring(rowIndex, length);
            rowIndex += length;
            columnNumber += 1;
            var columnValue = columnPortion.Trim();
            if (columnValue.Length == 0) {
                return null;
            }
            return double.Parse(columnValue);
        }

        private static double ParseDouble(String row, int length, ref int rowIndex, ref int columnNumber, ref string columnPortion) {
            columnPortion = row.Substring(rowIndex, length);
            rowIndex += length;
            columnNumber += 1;
            return double.Parse(columnPortion.Trim());
        }

        private static string ParseString(String row, int length, ref int rowIndex, ref int columnNumber, ref string columnPortion) {
            if (length < 0) {
                length = row.Length - rowIndex;
            }

            columnPortion = row.Substring(rowIndex, length);
            rowIndex += length;
            columnNumber += 1;
            return columnPortion.Trim();
        }

        public event EventHandler<MPCParseErrorDetail> ParseError;

        public static async Task<MPCCometResponse> GetFromHttpUri(string uri) {
            var client = new HttpClient();
            var cometsStream = await client.GetStreamAsync(uri);
            var streamReader = new StreamReader(cometsStream);
            return new MPCCometResponse(streamReader, new List<IDisposable> { cometsStream, client });
        }
    }

    public class MPCAccessor : IMPCAccessor {
        public const string comets_url = "https://www.minorplanetcenter.net/iau/MPCORB/CometEls.txt";

        public Task<MPCCometResponse> GetCometElements() {
            return MPCCometResponse.GetFromHttpUri(comets_url);
        }

        public Task<DateTime> GetCometElementsLastModified() {
            return GetLastModifiedFromURL(comets_url);
        }

        private async Task<DateTime> GetLastModifiedFromURL(string url) {
            using (var client = new HttpClient()) {
                var headMessage = new HttpRequestMessage(HttpMethod.Head, url);
                var result = await client.SendAsync(headMessage);
                var lastModified = result.Content.Headers.LastModified;
                return lastModified?.UtcDateTime ?? DateTime.MinValue;
            }
        }
    }
}