#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using FlatFiles;
using FlatFiles.TypeMapping;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public abstract class JPLOrbitalElements : IOrbitalElementsSource {
        public string name { get; set; }
        public int epoch { get; set; }
        public double e { get; set; }
        public double i { get; set; }
        public double w { get; set; }
        public double node { get; set; }
        public string ref_ { get; set; }
        public string Name => name;

        public abstract OrbitalElements ToOrbitalElements();
    }

    public class JPLCometElements : JPLOrbitalElements {
        public double q { get; set; }
        public double tp { get; set; }

        public override OrbitalElements ToOrbitalElements() {
            return new OrbitalElements(name) {
                PrimaryGravitationalParameter = GravitationalParameter.Sun,
                SecondaryGravitationalParameter = GravitationalParameter.Zero,
                Epoch_jd = epoch + 2400000.5,
                q_Perihelion_au = q,
                e_Eccentricity = e,
                i_Inclination_rad = i * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = w * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = node * AstrometricConstants.RAD_PER_DEG,
                tp_PeriapsisTime_jd = JPLAccessor.CalendarDateAndFractionToJulian(tp)
            };
        }

        public override string ToString() {
            return $"{{{nameof(q)}={q.ToString()}, {nameof(tp)}={tp.ToString()}, {nameof(name)}={name}, {nameof(epoch)}={epoch.ToString()}, {nameof(e)}={e.ToString()}, {nameof(i)}={i.ToString()}, {nameof(w)}={w.ToString()}, {nameof(node)}={node.ToString()}, {nameof(ref_)}={ref_}, {nameof(Name)}={Name}}}";
        }
    }

    public abstract class JPLAsteroidElementsBase : JPLOrbitalElements {
        public double a { get; set; }
        public double M { get; set; }
        public double H { get; set; }
        public double G { get; set; }

        public abstract string GetName();

        public override OrbitalElements ToOrbitalElements() {
            return new OrbitalElements(GetName()) {
                PrimaryGravitationalParameter = GravitationalParameter.Sun,
                SecondaryGravitationalParameter = GravitationalParameter.Zero,
                Epoch_jd = epoch + 2400000.5,
                M_MeanAnomalyAtEpoch = M * AstrometricConstants.RAD_PER_DEG,
                a_SemiMajorAxis_au = a,
                e_Eccentricity = e,
                i_Inclination_rad = i * AstrometricConstants.RAD_PER_DEG,
                w_ArgOfPerihelion_rad = w * AstrometricConstants.RAD_PER_DEG,
                node_LongitudeOfAscending_rad = node * AstrometricConstants.RAD_PER_DEG
            };
        }
    }

    public class JPLUnnumberedAsteroidElements : JPLAsteroidElementsBase {

        public override string GetName() {
            return name;
        }
    }

    public class JPLNumberedAsteroidElements : JPLAsteroidElementsBase {
        public int number { get; set; }

        public override string GetName() {
            return $"{number}/{name}";
        }
    }

    public class JPLVectorTableRow {
        public double epochJd { get; set; }
        public RectangularPV positionVelocity { get; set; }
    }

    public class JPLVectorTable {

        public JPLVectorTable(string name) {
            this.name = name;
        }

        public string name { get; private set; }

        public List<JPLVectorTableRow> rows { get; private set; } = new List<JPLVectorTableRow>();

        public PVTable ToPVTable() {
            return new PVTable(name) {
                Rows = rows.Select(r => new PVTableRow() {
                    Epoch_jd = r.epochJd,
                    X = r.positionVelocity.Position.X,
                    Y = r.positionVelocity.Position.Y,
                    Z = r.positionVelocity.Position.Z,
                    VelocityX = r.positionVelocity.Velocity.X,
                    VelocityY = r.positionVelocity.Velocity.Y,
                    VelocityZ = r.positionVelocity.Velocity.Z,
                }).ToList()
            };
        }
    }

    public class JPLParseErrorDetail {
        public int RecordNumber { get; set; }
        public string ColumnName { get; set; }
        public string ErrorValue { get; set; }
        public string ErrorMessage { get; set; }
    }

    public abstract class JPLRecordResponse<T> : IDisposable {
        private readonly List<IDisposable> disposeOnCompletion;

        public JPLRecordResponse(List<IDisposable> disposeOnCompletion = null) {
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

    public class JPLCometResponse : JPLRecordResponse<JPLCometElements> {

        public JPLCometResponse(StreamReader streamReader, List<IDisposable> disposeOnCompletion = null) {
            try {
                var headerLine = streamReader.ReadLine();
                var spacingLine = streamReader.ReadLine();
                var headerLengths = spacingLine.Split(' ').Select(s => s.Length).ToArray();
                if (headerLengths.Length != 9) {
                    throw new Exception($"Expected 9 header entries");
                }

                var mapper = FixedLengthTypeMapper.Define(() => new JPLCometElements());
                mapper.Property(x => x.name, headerLengths[0] + 1);
                mapper.Property(x => x.epoch, headerLengths[1] + 1);
                mapper.Property(x => x.q, headerLengths[2] + 1);
                mapper.Property(x => x.e, headerLengths[3] + 1);
                mapper.Property(x => x.i, headerLengths[4] + 1);
                mapper.Property(x => x.w, headerLengths[5] + 1);
                mapper.Property(x => x.node, headerLengths[6] + 1);
                mapper.Property(x => x.tp, headerLengths[7] + 1);
                mapper.Property(x => x.ref_, Window.Trailing);
                var options = new FixedLengthOptions() {
                    IsFirstRecordHeader = false,
                    FormatProvider = CultureInfo.InvariantCulture
                };
                var recordReader = mapper.GetReader(streamReader, options);
                recordReader.ColumnError += (sender, e) => {
                    var columnContext = e.ColumnContext;
                    var detail = new JPLParseErrorDetail() {
                        RecordNumber = columnContext.RecordContext.PhysicalRecordNumber,
                        ColumnName = columnContext.ColumnDefinition.ColumnName,
                        ErrorValue = e.ColumnValue?.ToString(),
                        ErrorMessage = e.Exception.InnerException?.Message ?? e.Exception.Message
                    };
                    e.IsHandled = false;
                    e.Substitution = null;
                    ParseError?.Invoke(recordReader, detail);
                };

                this.Response = recordReader.ReadAll();
            } catch (Exception) {
                streamReader?.Dispose();
                if (disposeOnCompletion != null) {
                    disposeOnCompletion.ForEach(c => c.Dispose());
                }
                throw;
            }
        }

        public event EventHandler<JPLParseErrorDetail> ParseError;

        public static async Task<JPLCometResponse> GetFromHttpUri(string uri) {
            var client = new HttpClient();
            var cometsStream = await client.GetStreamAsync(uri);
            var streamReader = new StreamReader(cometsStream);
            return new JPLCometResponse(streamReader, new List<IDisposable> { cometsStream, client });
        }
    }

    public class JPLUnnumberedAsteroidResponse : JPLRecordResponse<JPLUnnumberedAsteroidElements> {

        public JPLUnnumberedAsteroidResponse(StreamReader streamReader, List<IDisposable> disposeOnCompletion = null) {
            try {
                var headerLine = streamReader.ReadLine();
                var spacingLine = streamReader.ReadLine();
                var headerLengths = spacingLine.Split(' ').Select(s => s.Length).ToArray();
                if (headerLengths.Length != 11) {
                    throw new Exception("Expected 11 header entries for un-numbered asteroid elements");
                }

                var mapper = FixedLengthTypeMapper.Define(() => new JPLUnnumberedAsteroidElements());
                mapper.Property(x => x.name, headerLengths[0] + 1);
                mapper.Property(x => x.epoch, headerLengths[1] + 1);
                mapper.Property(x => x.a, headerLengths[2] + 1);
                mapper.Property(x => x.e, headerLengths[3] + 1);
                mapper.Property(x => x.i, headerLengths[4] + 1);
                mapper.Property(x => x.w, headerLengths[5] + 1);
                mapper.Property(x => x.node, headerLengths[6] + 1);
                mapper.Property(x => x.M, headerLengths[7] + 1);
                mapper.Property(x => x.H, headerLengths[8] + 1);
                mapper.Property(x => x.H, headerLengths[9] + 1);
                mapper.Property(x => x.ref_, Window.Trailing);
                var options = new FixedLengthOptions() {
                    IsFirstRecordHeader = false,
                    FormatProvider = CultureInfo.InvariantCulture
                };
                var recordReader = mapper.GetReader(streamReader, options);
                recordReader.ColumnError += (sender, e) => {
                    var columnContext = e.ColumnContext;
                    var detail = new JPLParseErrorDetail() {
                        RecordNumber = columnContext.RecordContext.PhysicalRecordNumber,
                        ColumnName = columnContext.ColumnDefinition.ColumnName,
                        ErrorValue = e.ColumnValue?.ToString(),
                        ErrorMessage = e.Exception.InnerException?.Message ?? e.Exception.Message
                    };
                    e.IsHandled = false;
                    e.Substitution = null;
                    ParseError?.Invoke(recordReader, detail);
                };

                this.Response = recordReader.ReadAll();
            } catch (Exception) {
                streamReader?.Dispose();
                if (disposeOnCompletion != null) {
                    disposeOnCompletion.ForEach(c => c.Dispose());
                }
                throw;
            }
        }

        public event EventHandler<JPLParseErrorDetail> ParseError;

        public static async Task<JPLUnnumberedAsteroidResponse> GetFromHttpUri(string uri) {
            var client = new HttpClient();
            var stream = await client.GetStreamAsync(uri);
            if (uri.EndsWith(".gz")) {
                stream = new GZipStream(stream, CompressionMode.Decompress, false);
            }
            var streamReader = new StreamReader(stream);
            return new JPLUnnumberedAsteroidResponse(streamReader, new List<IDisposable> { stream, client });
        }
    }

    public class JPLNumberedAsteroidResponse : JPLRecordResponse<JPLNumberedAsteroidElements> {

        public JPLNumberedAsteroidResponse(StreamReader streamReader, List<IDisposable> disposeOnCompletion = null) {
            try {
                var headerLine = streamReader.ReadLine();
                var spacingLine = streamReader.ReadLine();
                var headerLengths = spacingLine.Split(' ').Select(s => s.Length).ToArray();
                if (headerLengths.Length != 12) {
                    throw new Exception("Expected 12 header entries for numbered asteroid elements");
                }

                var mapper = FixedLengthTypeMapper.Define(() => new JPLNumberedAsteroidElements());
                mapper.Property(x => x.number, headerLengths[0] + 1);
                mapper.Property(x => x.name, headerLengths[1] + 1);
                mapper.Property(x => x.epoch, headerLengths[2] + 1);
                mapper.Property(x => x.a, headerLengths[3] + 1);
                mapper.Property(x => x.e, headerLengths[4] + 1);
                mapper.Property(x => x.i, headerLengths[5] + 1);
                mapper.Property(x => x.w, headerLengths[6] + 1);
                mapper.Property(x => x.node, headerLengths[7] + 1);
                mapper.Property(x => x.M, headerLengths[8] + 1);
                mapper.Property(x => x.H, headerLengths[9] + 1);
                mapper.Property(x => x.H, headerLengths[10] + 1);
                mapper.Property(x => x.ref_, Window.Trailing);
                var options = new FixedLengthOptions() {
                    IsFirstRecordHeader = false,
                    FormatProvider = CultureInfo.InvariantCulture
                };
                var recordReader = mapper.GetReader(streamReader, options);
                recordReader.ColumnError += (sender, e) => {
                    var columnContext = e.ColumnContext;
                    var detail = new JPLParseErrorDetail() {
                        RecordNumber = columnContext.RecordContext.PhysicalRecordNumber,
                        ColumnName = columnContext.ColumnDefinition.ColumnName,
                        ErrorValue = e.ColumnValue?.ToString(),
                        ErrorMessage = e.Exception.InnerException?.Message ?? e.Exception.Message
                    };
                    e.IsHandled = false;
                    e.Substitution = null;
                    ParseError?.Invoke(recordReader, detail);
                };

                this.Response = recordReader.ReadAll();
            } catch (Exception) {
                streamReader?.Dispose();
                if (disposeOnCompletion != null) {
                    disposeOnCompletion.ForEach(c => c.Dispose());
                }
                throw;
            }
        }

        public event EventHandler<JPLParseErrorDetail> ParseError;

        public static async Task<JPLNumberedAsteroidResponse> GetFromHttpUri(string uri) {
            var client = new HttpClient();
            var stream = await client.GetStreamAsync(uri);
            if (uri.EndsWith(".gz")) {
                stream = new GZipStream(stream, CompressionMode.Decompress, false);
            }
            var streamReader = new StreamReader(stream);
            return new JPLNumberedAsteroidResponse(streamReader, new List<IDisposable> { stream, client });
        }
    }

    public class JPLAccessor : IJPLAccessor {

        // Small bodies can be downloaded at https://ssd.jpl.nasa.gov/sb/elem_tables.html
        public const string comets_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.COMET";

        public const string unnumbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.UNNUM.gz";
        public const string numbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.NUMBR.gz";

        public Task<DateTime> GetCometElementsLastModified() {
            return GetLastModifiedFromURL(comets_url);
        }

        public Task<DateTime> GetUnnumberedAsteroidsElementsLastModified() {
            return GetLastModifiedFromURL(unnumbered_asteroids_url);
        }

        public Task<DateTime> GetNumberedAsteroidsLastModified() {
            return GetLastModifiedFromURL(numbered_asteroids_url);
        }

        private async Task<DateTime> GetLastModifiedFromURL(string url) {
            using (var client = new HttpClient()) {
                var headMessage = new HttpRequestMessage(HttpMethod.Head, url);
                var result = await client.SendAsync(headMessage);
                var lastModified = result.Content.Headers.LastModified;
                return lastModified?.UtcDateTime ?? DateTime.MinValue;
            }
        }

        public Task<JPLCometResponse> GetCometElements() {
            return JPLCometResponse.GetFromHttpUri(comets_url);
        }

        public Task<JPLNumberedAsteroidResponse> GetNumberedAsteroidElements() {
            return JPLNumberedAsteroidResponse.GetFromHttpUri(numbered_asteroids_url);
        }

        public Task<JPLUnnumberedAsteroidResponse> GetUnnumberedAsteroidElements() {
            return JPLUnnumberedAsteroidResponse.GetFromHttpUri(unnumbered_asteroids_url);
        }

        public async Task<JPLVectorTable> GetJWSTVectorTable(DateTime asof, TimeSpan lookahead) {
            var startDate = asof - TimeSpan.FromHours(1);
            var endDate = asof + lookahead;
            var queryString = $"https://ssd.jpl.nasa.gov/api/horizons.api?format=text&COMMAND='JWST'&OBJ_DATA='NO'&MAKE_EPHEM='YES'&EPHEM_TYPE='VECTOR'&CENTER='500@399'&START_TIME='{startDate}'&STOP_TIME='{endDate}'&STEP_SIZE='1%20h'&QUANTITIES='1'&OUT_UNITS='AU-D'";
            using (var client = new HttpClient()) {
                var data = await client.GetStringAsync(queryString);
                var startOfEntry = "$$SOE";
                var endOfEntry = "$$EOE";

                var startOfEntryIndex = data.IndexOf(startOfEntry);
                if (startOfEntryIndex < 0) {
                    throw new Exception($"Response from JPL horizons did not contain the expected $$SOE marker");
                }

                var endOfEntryIndex = data.IndexOf(endOfEntry, startOfEntryIndex);
                if (endOfEntryIndex < 0) {
                    throw new Exception($"Response from JPL horizons did not contain the expected $$EOE marker");
                }

                var result = new JPLVectorTable("JWST");
                var entryText = data.Substring(startOfEntryIndex + startOfEntry.Length, endOfEntryIndex - startOfEntryIndex - startOfEntry.Length).Trim();
                var formatProvider = CultureInfo.InvariantCulture;
                using (var sr = new StringReader(entryText)) {
                    string firstLine;
                    while ((firstLine = sr.ReadLine()) != null) {
                        var firstLineParts = firstLine.Split('=');
                        var epochJd = Convert.ToDouble(firstLineParts[0], formatProvider);
                        var secondLine = sr.ReadLine();
                        var xIndex = secondLine.IndexOf("X =");
                        var yIndex = secondLine.IndexOf("Y =");
                        var zIndex = secondLine.IndexOf("Z =");
                        var x = Convert.ToDouble(secondLine.Substring(xIndex + 3, yIndex - xIndex - 3), formatProvider);
                        var y = Convert.ToDouble(secondLine.Substring(yIndex + 3, zIndex - yIndex - 3), formatProvider);
                        var z = Convert.ToDouble(secondLine.Substring(zIndex + 3), formatProvider);

                        var thirdLine = sr.ReadLine();
                        var vxIndex = thirdLine.IndexOf("VX=");
                        var vyIndex = thirdLine.IndexOf("VY=");
                        var vzIndex = thirdLine.IndexOf("VZ=");
                        var vx = Convert.ToDouble(thirdLine.Substring(vxIndex + 3, vyIndex - vxIndex - 3), formatProvider);
                        var vy = Convert.ToDouble(thirdLine.Substring(vyIndex + 3, vzIndex - vyIndex - 3), formatProvider);
                        var vz = Convert.ToDouble(thirdLine.Substring(vzIndex + 3), formatProvider);

                        // Ignore the 3rd line
                        _ = sr.ReadLine();

                        var position = new RectangularCoordinates(x, y, z);
                        var velocity = new RectangularCoordinates(vx, vy, vz);
                        result.rows.Add(new JPLVectorTableRow() {
                            epochJd = epochJd,
                            positionVelocity = new RectangularPV(position, velocity)
                        });
                    }
                    return result;
                }
            }
        }

        /// <summary>
        /// Converts a datetime formatted as yyyyMMdd.partial to a julian date. The partial is the ratio of a single day
        /// </summary>
        /// <param name="dateAndFraction">Datetime formatted as yyyyMMdd.partial</param>
        /// <returns>A julian date</returns>
        public static double CalendarDateAndFractionToJulian(double dateAndFraction) {
            var yyyyMMdd_abs = Math.Abs(dateAndFraction);
            var yyyyMMdd = (int)yyyyMMdd_abs;
            var dayPart = yyyyMMdd_abs - yyyyMMdd;
            var year = (short)(yyyyMMdd / 10000);
            if (dateAndFraction < 0) {
                year = (short)-year;
            }
            var month = (short)((yyyyMMdd / 100) % 100);
            var day = (short)(yyyyMMdd % 100);
            return NOVAS.JulianDate(year, month, day, dayPart * 24);
        }
    }
}