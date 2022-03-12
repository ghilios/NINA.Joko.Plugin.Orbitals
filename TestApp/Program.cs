#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ASCOM.DriverAccess;
using Accord.Math;
using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Data.Entity;
using System.Net.Http;
using FlatFiles;
using System.IO;
using FlatFiles.TypeMapping;

namespace TestApp {

    public class Blog {
        public int BlogId { get; set; }
        public string Name { get; set; }

        public virtual List<Post> Posts { get; set; }
    }

    public class Post {
        public int PostId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }

        public int BlogId { get; set; }
        public virtual Blog Blog { get; set; }
    }

    public class BloggingContext : DbContext {
        public DbSet<Blog> Blogs { get; set; }
        public DbSet<Post> Posts { get; set; }
    }

    public class JPLCometElements {
        public string name { get; set; }
        public int epoch { get; set; }
        public double q { get; set; }
        public double e { get; set; }
        public double i { get; set; }
        public double w { get; set; }
        public double node { get; set; }
        public DateTime tp { get; set; }
        public bool tp_BC { get; set; } = false;
        public string ref_ { get; set; }

        public override string ToString() {
            return $"{{{nameof(name)}={name}, {nameof(epoch)}={epoch.ToString()}, {nameof(q)}={q.ToString()}, {nameof(e)}={e.ToString()}, {nameof(i)}={i.ToString()}, {nameof(w)}={w.ToString()}, {nameof(node)}={node.ToString()}, {nameof(tp)}={tp.ToString()}, {nameof(tp_BC)}={tp_BC.ToString()}, {nameof(ref_)}={ref_}}}";
        }
    }

    internal class Program {
        /*
        private static void Main(string[] args) {
            using (var db = new BloggingContext()) {
                // Create and save a new Blog
                Console.Write("Enter a name for a new Blog: ");
                var name = Console.ReadLine();

                var blog = new Blog { Name = name };
                db.Blogs.Add(blog);
                db.SaveChanges();

                // Display all Blogs from the database
                var query = from b in db.Blogs
                            orderby b.Name
                            select b;

                Console.WriteLine("All blogs in the database:");
                foreach (var item in query) {
                    Console.WriteLine(item.Name);
                }

                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }
        */

        public class ErrorDetail {
            public int RecordNumber { get; set; }
            public string ColumnName { get; set; }
            public string ErrorValue { get; set; }
            public string ErrorMessage { get; set; }
        }

        private static async Task Main(string[] args) {
            // Small bodies can be downloaded at https://ssd.jpl.nasa.gov/sb/elem_tables.html
            const string comets_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.COMET";
            const string unnumbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.UNNUM.gz";
            const string numbered_asteroids_url = "https://ssd.jpl.nasa.gov/dat/ELEMENTS.NUMBR.gz";

            using (var client = new HttpClient()) {
                var headMessage = new HttpRequestMessage(HttpMethod.Head, comets_url);
                var result = await client.SendAsync(headMessage);
                var lastModified = result.Content.Headers.LastModified;
                var lmd = lastModified?.UtcDateTime;
                Console.WriteLine();

                using (var cometsStream = await client.GetStreamAsync(comets_url)) {
                    using (var streamReader = new StreamReader(cometsStream)) {
                        var headerLine = streamReader.ReadLine();
                        var spacingLine = streamReader.ReadLine();
                        var headerLengths = spacingLine.Split(' ').Select(s => s.Length).ToArray();
                        // Assert this is 9 elements long
                        Console.WriteLine();

                        var firstLine = streamReader.ReadLine();
                        var mapper = FixedLengthTypeMapper.Define(() => new JPLCometElements());
                        mapper.Property(x => x.name, headerLengths[0] + 1);
                        mapper.Property(x => x.epoch, headerLengths[1] + 1);
                        mapper.Property(x => x.q, headerLengths[2] + 1);
                        mapper.Property(x => x.e, headerLengths[3] + 1);
                        mapper.Property(x => x.i, headerLengths[4] + 1);
                        mapper.Property(x => x.w, headerLengths[5] + 1);
                        mapper.Property(x => x.node, headerLengths[6] + 1);
                        mapper.CustomMapping(new DoubleColumn("tp"), headerLengths[7] + 1)
                            .WithReader((c, p, v) => {
                                var value = (double)v;
                                if (value < 0) {
                                    p.tp_BC = true;
                                    // value = Math.Abs(value);
                                }

                                var yyyyMMdd = (int)value;
                                var dayPart = value - yyyyMMdd;
                                var year = yyyyMMdd / 10000;
                                var month = (yyyyMMdd / 100) % 100;
                                var day = yyyyMMdd % 100;
                                p.tp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromDays(dayPart);
                            });
                        mapper.Property(x => x.ref_, Window.Trailing);

                        var options = new FixedLengthOptions() {
                            IsFirstRecordHeader = false,
                        };

                        var recordReader = mapper.GetReader(streamReader, options);

                        var errorDetails = new List<ErrorDetail>();
                        recordReader.ColumnError += (sender, e) => {
                            var columnContext = e.ColumnContext;
                            var detail = new ErrorDetail() {
                                RecordNumber = columnContext.RecordContext.PhysicalRecordNumber,
                                ColumnName = columnContext.ColumnDefinition.ColumnName,
                                ErrorValue = e.ColumnValue?.ToString(),
                                ErrorMessage = e.Exception.InnerException?.Message ?? e.Exception.Message
                            };
                            errorDetails.Add(detail);
                            e.IsHandled = false;
                            e.Substitution = null;  // May not work for non-nullable value types
                        };

                        foreach (var comet in recordReader.ReadAll()) {
                            Console.WriteLine(comet);
                        }
                    }
                }

                /*

                */
            }
        }
    }
}