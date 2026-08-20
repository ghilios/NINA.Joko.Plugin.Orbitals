#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    /// <summary>
    /// Thrown when a file the user picked is not one of the recognised element formats.
    /// The message deliberately quotes the first line of the file, because by far the most
    /// common failure is having saved the site's HTML error page instead of the raw data.
    /// </summary>
    public class OrbitalElementsImportException : Exception {

        public OrbitalElementsImportException(string message) : base(message) {
        }
    }

    /// <summary>
    /// Loads orbital elements from a file the user supplied rather than from the network.
    ///
    /// This exists because observatory PCs behind VPN gateways get their IPs blocked by the
    /// MPC servers, so the download simply fails and the user has no recourse. They can
    /// fetch the file on another machine and hand it over here.
    ///
    /// The imported data becomes that feed's store, so it participates in the "Both" merge
    /// exactly as a download would -- a blocked user still gets JPL over the network and MPC
    /// from their file.
    /// </summary>
    public static class OrbitalElementsFileImporter {

        public class ImportResult {

            public ImportResult(OrbitalElementsSourceEnum source, IReadOnlyList<IOrbitalElementsSource> elements) {
                this.Source = source;
                this.Elements = elements;
            }

            public OrbitalElementsSourceEnum Source { get; }
            public IReadOnlyList<IOrbitalElementsSource> Elements { get; }
            public int Count => Elements.Count;
        }

        private static readonly byte[] GzipMagic = new byte[] { 0x1F, 0x8B };

        /// <summary>
        /// Reads an element file, detecting its format from the content rather than the
        /// extension -- someone troubleshooting at 2am should not have to know, and the
        /// files are trivially distinguishable anyway.
        /// </summary>
        public static ImportResult Import(string path, OrbitalObjectTypeEnum objectType) {
            if (!File.Exists(path)) {
                throw new OrbitalElementsImportException($"File not found: {path}");
            }

            var text = ReadAllTextMaybeGzipped(path);
            if (string.IsNullOrWhiteSpace(text)) {
                throw new OrbitalElementsImportException($"{Path.GetFileName(path)} is empty.");
            }

            var format = DetectFormat(text, objectType);
            switch (format) {
                case DetectedFormat.JplComet: {
                        using (var response = new JPLCometResponse(ToReader(text))) {
                            return new ImportResult(OrbitalElementsSourceEnum.JPL, response.Response.Cast<IOrbitalElementsSource>().ToList());
                        }
                    }
                case DetectedFormat.MpcComet: {
                        using (var response = new MPCCometResponse(ToReader(text))) {
                            return new ImportResult(OrbitalElementsSourceEnum.MPC, response.Response.Cast<IOrbitalElementsSource>().ToList());
                        }
                    }
                case DetectedFormat.JplNumberedAsteroid: {
                        using (var response = new JPLNumberedAsteroidResponse(ToReader(text))) {
                            return new ImportResult(OrbitalElementsSourceEnum.JPL, response.Response.Cast<IOrbitalElementsSource>().ToList());
                        }
                    }
                case DetectedFormat.JplUnnumberedAsteroid: {
                        using (var response = new JPLUnnumberedAsteroidResponse(ToReader(text))) {
                            return new ImportResult(OrbitalElementsSourceEnum.JPL, response.Response.Cast<IOrbitalElementsSource>().ToList());
                        }
                    }
                default:
                    throw new OrbitalElementsImportException(BuildUnrecognisedMessage(path, text, objectType));
            }
        }

        private enum DetectedFormat {
            Unknown,
            JplComet,
            MpcComet,
            JplNumberedAsteroid,
            JplUnnumberedAsteroid
        }

        /// <summary>
        /// JPL files lead with a column-name header and a row of dashes whose lengths define
        /// the fixed-width columns; the header names tell the three JPL variants apart. MPC
        /// files have no header at all -- every line is a record.
        /// </summary>
        private static DetectedFormat DetectFormat(string text, OrbitalObjectTypeEnum objectType) {
            var lines = text.Split('\n');
            var header = lines.Length > 0 ? lines[0] : string.Empty;
            var second = lines.Length > 1 ? lines[1] : string.Empty;

            var looksJpl = second.TrimStart().StartsWith("---") && header.IndexOf("Epoch", StringComparison.OrdinalIgnoreCase) >= 0;
            if (looksJpl) {
                var hasQ = System.Text.RegularExpressions.Regex.IsMatch(header, @"(^|\s)q(\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (hasQ) {
                    return DetectedFormat.JplComet;
                }
                // Asteroid bundles differ only by a leading number column.
                return header.TrimStart().StartsWith("Num", StringComparison.OrdinalIgnoreCase)
                    ? DetectedFormat.JplNumberedAsteroid
                    : DetectedFormat.JplUnnumberedAsteroid;
            }

            if (objectType == OrbitalObjectTypeEnum.Comet && LooksLikeMpcCometRecord(header)) {
                return DetectedFormat.MpcComet;
            }

            return DetectedFormat.Unknown;
        }

        /// <summary>
        /// An MPC comet record begins with either a 4-digit periodic number plus an orbit
        /// type letter (e.g. "0220P") or a blank number field followed by a packed
        /// provisional designation (e.g. "    CJ95O010"), and carries a 4-digit perihelion
        /// year at columns 15-18.
        /// </summary>
        private static bool LooksLikeMpcCometRecord(string line) {
            if (line == null || line.Length < 30) {
                return false;
            }
            var orbitType = line[4];
            if ("PCDXAI".IndexOf(orbitType) < 0) {
                return false;
            }
            var year = line.Substring(14, 4).Trim();
            return year.Length == 4 && int.TryParse(year, out _);
        }

        private static string BuildUnrecognisedMessage(string path, string text, OrbitalObjectTypeEnum objectType) {
            var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
            if (firstLine.Length > 120) {
                firstLine = firstLine.Substring(0, 120) + "...";
            }

            var expected = objectType == OrbitalObjectTypeEnum.Comet
                ? "MPC CometEls.txt or JPL ELEMENTS.COMET"
                : "JPL ELEMENTS.NUMBR or ELEMENTS.UNNUM";

            return $"Could not read {Path.GetFileName(path)} as {objectType.ToDescriptionString()} elements. " +
                   $"Expected {expected} format. First line was: \"{firstLine}\"";
        }

        /// <summary>The parsers take a StreamReader, so wrap the already-decoded text back up.</summary>
        private static StreamReader ToReader(string text) =>
            new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), Encoding.UTF8);

        /// <summary>
        /// Reads the file, transparently decompressing it when gzipped. The JPL asteroid
        /// bundles ship as .gz, and users often keep them that way.
        /// </summary>
        private static string ReadAllTextMaybeGzipped(string path) {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                var magic = new byte[2];
                var read = fs.Read(magic, 0, 2);
                fs.Seek(0, SeekOrigin.Begin);

                if (read == 2 && magic[0] == GzipMagic[0] && magic[1] == GzipMagic[1]) {
                    using (var gs = new GZipStream(fs, CompressionMode.Decompress))
                    using (var reader = new StreamReader(gs, Encoding.UTF8)) {
                        return reader.ReadToEnd();
                    }
                }

                using (var reader = new StreamReader(fs, Encoding.UTF8)) {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
