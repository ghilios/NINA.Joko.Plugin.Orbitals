#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Enums;
using System;
using System.IO;
using System.Text.Json;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    /// <summary>
    /// How a single feed's element store was obtained. Persisted as a small JSON sidecar
    /// next to the store rather than inside it, because the store is a length-prefixed
    /// protobuf *sequence* with no header record to hang this off.
    ///
    /// Absence of the sidecar is not an error -- stores written before this existed are
    /// treated as downloads, with the timestamp falling back to the file's mtime.
    /// </summary>
    public class OrbitalElementsFeedMetadata {

        public OrbitalElementsSourceEnum Source { get; set; } = OrbitalElementsSourceEnum.Unknown;

        public bool Imported { get; set; }

        public DateTime AcquiredAt { get; set; } = DateTime.MinValue;

        /// <summary>File name (not full path) the data was imported from, when <see cref="Imported"/>.</summary>
        public string SourceFileName { get; set; }

        public int RecordCount { get; set; }

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
            WriteIndented = true
        };

        public static string GetPath(string storePath) => storePath + ".meta.json";

        public static void Save(string storePath, OrbitalElementsFeedMetadata metadata) {
            try {
                File.WriteAllText(GetPath(storePath), JsonSerializer.Serialize(metadata, SerializerOptions));
            } catch (Exception e) {
                // Non-fatal: the elements themselves are already written, and a missing
                // sidecar degrades to "downloaded at <file mtime>".
                Logger.Warning($"Failed to write feed metadata for {storePath}. {e.Message}");
            }
        }

        /// <summary>
        /// Reads the sidecar, or synthesizes one for a store that predates it. Returns null
        /// only when the store itself is absent.
        /// </summary>
        public static OrbitalElementsFeedMetadata Load(string storePath, OrbitalElementsSourceEnum source) {
            if (!File.Exists(storePath)) {
                return null;
            }

            var metadataPath = GetPath(storePath);
            if (File.Exists(metadataPath)) {
                try {
                    var loaded = JsonSerializer.Deserialize<OrbitalElementsFeedMetadata>(File.ReadAllText(metadataPath));
                    if (loaded != null) {
                        if (loaded.Source == OrbitalElementsSourceEnum.Unknown) {
                            loaded.Source = source;
                        }
                        return loaded;
                    }
                } catch (Exception e) {
                    Logger.Warning($"Failed to read feed metadata at {metadataPath}, falling back to file timestamp. {e.Message}");
                }
            }

            return new OrbitalElementsFeedMetadata() {
                Source = source,
                Imported = false,
                AcquiredAt = File.GetLastWriteTime(storePath)
            };
        }

        public static void Delete(string storePath) {
            try {
                var metadataPath = GetPath(storePath);
                if (File.Exists(metadataPath)) {
                    File.Delete(metadataPath);
                }
            } catch (Exception e) {
                Logger.Warning($"Failed to delete feed metadata for {storePath}. {e.Message}");
            }
        }

        /// <summary>Short human-readable acquisition description, e.g. "downloaded 2026-08-19".</summary>
        public string DescribeAcquisition() {
            var when = AcquiredAt <= DateTime.MinValue
                ? "unknown date"
                : AcquiredAt.ToString("d", OrbitalsPlugin.SystemCultureInfo);
            return Imported && !string.IsNullOrEmpty(SourceFileName)
                ? $"imported from {SourceFileName} on {when}"
                : Imported ? $"imported {when}" : $"downloaded {when}";
        }

        public override string ToString() {
            return $"{{{nameof(Source)}={Source}, {nameof(Imported)}={Imported}, {nameof(AcquiredAt)}={AcquiredAt}, {nameof(SourceFileName)}={SourceFileName}, {nameof(RecordCount)}={RecordCount}}}";
        }
    }
}
