using FlatFiles;
using FlatFiles.TypeMapping;
using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static TestApp.Kepler;

namespace TestApp {
    public class JPLCometElements {
        public string name { get; set; }
        public int epoch { get; set; }
        public double q { get; set; }
        public double e { get; set; }
        public double i { get; set; }
        public double w { get; set; }
        public double node { get; set; }
        public double tp { get; set; }
        public string ref_ { get; set; }

        public override string ToString() {
            return $"{{{nameof(name)}={name}, {nameof(epoch)}={epoch.ToString()}, {nameof(q)}={q.ToString()}, {nameof(e)}={e.ToString()}, {nameof(i)}={i.ToString()}, {nameof(w)}={w.ToString()}, {nameof(node)}={node.ToString()}, {nameof(tp)}={tp.ToString()}, {nameof(ref_)}={ref_}}}";
        }

        public OrbitalElements ToOrbitalElements() {
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
        protected virtual void OnDispose() { }

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

                var firstLine = streamReader.ReadLine();
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

        public static async Task<JPLCometResponse> GetCometsFromHttpUri(string uri) {
            var client = new HttpClient();
            var cometsStream = await client.GetStreamAsync(uri);
            var streamReader = new StreamReader(cometsStream);
            return new JPLCometResponse(streamReader, new List<IDisposable> { cometsStream, client });
        }
    }

    public class JPLAccessor {
        // Small bodies can be downloaded at https://ssd.jpl.nasa.gov/sb/elem_tables.html
        public const string comets_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.COMET";
        public const string unnumbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.UNNUM.gz";
        public const string numbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.NUMBR.gz";

        public async Task<DateTime> GetCometElementsLastModified() {
            using (var client = new HttpClient()) {
                var headMessage = new HttpRequestMessage(HttpMethod.Head, comets_url);
                var result = await client.SendAsync(headMessage);
                var lastModified = result.Content.Headers.LastModified;
                return lastModified?.UtcDateTime ?? DateTime.MinValue;
            }
        }

        public Task<JPLCometResponse> GetCometElements() {
            return JPLCometResponse.GetCometsFromHttpUri(comets_url);
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
