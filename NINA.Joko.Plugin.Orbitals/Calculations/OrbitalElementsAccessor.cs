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
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Data.Linq;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class OrbitalElementsAccessor : IOrbitalElementsAccessor {

        private class OrbitalElementsBackend : IDisposable {

            private OrbitalElementsBackend(OrbitalObjectTypeEnum objectType, DateTime lastModified, TrigramStringMap<OrbitalElements> lookup) {
                this.ObjectType = objectType;
                this.Lookup = lookup;
                this.LastModified = lastModified;
            }

            public OrbitalObjectTypeEnum ObjectType { get; private set; }
            public TrigramStringMap<OrbitalElements> Lookup { get; private set; }
            public DateTime LastModified { get; private set; }

            public static OrbitalElementsBackend Create(OrbitalObjectTypeEnum objectType, DateTime lastModified, IEnumerable<OrbitalElements> objects, CancellationToken ct) {
                var lookup = new TrigramStringMap<OrbitalElements>(objectType.ToString());
                lookup.AddRange(o => o.Name, objects);
                return new OrbitalElementsBackend(objectType, lastModified, lookup);
            }

            public void Dispose() {
                Lookup?.Dispose();
            }
        }

        private readonly ManualResetEvent loadedEvent;
        private readonly IOrbitalsOptions options;
        private readonly object backendLock = new object();
        private readonly Dictionary<OrbitalObjectTypeEnum, OrbitalElementsBackend> backendsByType = new Dictionary<OrbitalObjectTypeEnum, OrbitalElementsBackend>();
        private PVTable jwstVectorTable;

        public OrbitalElementsAccessor(IOrbitalsOptions options) {
            this.loadedEvent = new ManualResetEvent(false);
            this.options = options;
            foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectTypeEnum)).Cast<OrbitalObjectTypeEnum>()) {
                backendsByType.Add(objectType, CreateDefaultBackend(objectType));
            }

            options.PropertyChanged += Options_PropertyChanged;
        }

        private static OrbitalElementsBackend CreateDefaultBackend(OrbitalObjectTypeEnum objectType) {
            return OrbitalElementsBackend.Create(objectType, DateTime.MinValue, Enumerable.Empty<OrbitalElements>(), CancellationToken.None);
        }

        private void Options_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IOrbitalsOptions.CometAccessor)) {
                LoadObjectType(OrbitalObjectTypeEnum.Comet, CancellationToken.None);
            }
        }

        private void LoadJWST(CancellationToken ct) {
            var path = GetJWSTSavePath();
            if (!File.Exists(path)) {
                Logger.Info($"No JWST vector table loaded since no file was found at {path}");
                return;
            }

            try {
                ct.ThrowIfCancellationRequested();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var gs = new GZipStream(fs, CompressionMode.Decompress)) {
                    var vectorTable = ProtoBuf.Serializer.DeserializeWithLengthPrefix<PVTable>(gs, ProtoBuf.PrefixStyle.Base128, 1);
                    this.jwstVectorTable = vectorTable;
                    OnVectorTableUpdated(vectorTable);
                }
            } catch (OperationCanceledException) {
                return;
            } catch (Exception e) {
                Logger.Error($"Failed to load JWST vector table", e);
                Notification.ShowError($"Failed to load JWST vector table");
            }
        }

        private OrbitalElementsAccessorEnum GetAccessor(OrbitalObjectTypeEnum objectType) {
            if (objectType == OrbitalObjectTypeEnum.Comet) {
                return this.options.CometAccessor;
            } else {
                return OrbitalElementsAccessorEnum.JPL;
            }
        }

        /// <summary>
        /// Which feeds contribute to an object type under the current source policy. Only
        /// comets have a choice; asteroids are JPL-only.
        /// </summary>
        private OrbitalElementsSourceEnum[] GetContributingSources(OrbitalObjectTypeEnum objectType) {
            var accessor = GetAccessor(objectType);
            if (accessor == OrbitalElementsAccessorEnum.JPLAndMPC) {
                return new[] { OrbitalElementsSourceEnum.JPL, OrbitalElementsSourceEnum.MPC };
            }
            return new[] {
                accessor == OrbitalElementsAccessorEnum.MPC
                    ? OrbitalElementsSourceEnum.MPC
                    : OrbitalElementsSourceEnum.JPL
            };
        }

        /// <summary>
        /// Loads one feed store. Elements written before the Source field existed come back
        /// as Unknown, so they are stamped from the feed they were read out of -- existing
        /// caches therefore show correct provenance without needing a re-download.
        /// Returns null when that feed has no store yet.
        /// </summary>
        private List<OrbitalElements> LoadFeed(
            OrbitalObjectTypeEnum objectType, OrbitalElementsSourceEnum source, CancellationToken ct) {
            var path = GetFeedSavePath(objectType, source);
            if (!File.Exists(path)) {
                return null;
            }

            ct.ThrowIfCancellationRequested();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var gs = new GZipStream(fs, CompressionMode.Decompress)) {
                var loaded = new List<OrbitalElements>();
                foreach (var element in ProtoBuf.Serializer.DeserializeItems<OrbitalElements>(gs, ProtoBuf.PrefixStyle.Base128, 1)) {
                    ct.ThrowIfCancellationRequested();
                    if (element.Source == OrbitalElementsSourceEnum.Unknown) {
                        element.Source = source;
                    }
                    loaded.Add(element);
                }
                return loaded;
            }
        }

        private void LoadObjectType(OrbitalObjectTypeEnum objectType, CancellationToken ct) {
            try {
                ct.ThrowIfCancellationRequested();
                var sources = GetContributingSources(objectType);
                var feeds = new Dictionary<OrbitalElementsSourceEnum, List<OrbitalElements>>();
                var metadata = new Dictionary<OrbitalElementsSourceEnum, OrbitalElementsFeedMetadata>();
                foreach (var source in sources) {
                    var elements = LoadFeed(objectType, source, ct);
                    if (elements != null) {
                        feeds[source] = elements;
                        metadata[source] = OrbitalElementsFeedMetadata.Load(GetFeedSavePath(objectType, source), source);
                    }
                }

                if (feeds.Count == 0) {
                    Logger.Info($"No {objectType} orbital elements loaded since no store was found");
                    var empty = CreateDefaultBackend(objectType);
                    UpdateBackend(objectType, empty);
                    OnUpdated(objectType, empty, metadata);
                    return;
                }

                IEnumerable<OrbitalElements> combined;
                if (feeds.Count == 1) {
                    combined = feeds.Values.First();
                } else {
                    // "Both": the merge is derived here at load time rather than persisted as
                    // a third store, so a feed that failed to download simply contributes
                    // older data instead of disappearing.
                    feeds.TryGetValue(OrbitalElementsSourceEnum.JPL, out var jplFeed);
                    feeds.TryGetValue(OrbitalElementsSourceEnum.MPC, out var mpcFeed);
                    var result = CometElementsMerger.Merge(Wrap(jplFeed), Wrap(mpcFeed));
                    combined = result.Elements;
                    Logger.Info($"Merged {objectType} elements: {result}");
                }

                var lastModified = metadata.Count == 0
                    ? DateTime.MinValue
                    : metadata.Values.Max(x => x.AcquiredAt);
                var backend = OrbitalElementsBackend.Create(objectType, lastModified, combined, ct);
                UpdateBackend(objectType, backend);
                OnUpdated(objectType, backend, metadata);
            } catch (OperationCanceledException) {
                return;
            } catch (Exception e) {
                Logger.Error($"Failed to load {objectType} orbital elements", e);
                Notification.ShowError($"Failed to load {objectType} orbital elements");
            }
        }

        /// <summary>Adapts already-materialized elements back to the merger input contract.</summary>
        private static IEnumerable<IOrbitalElementsSource> Wrap(IEnumerable<OrbitalElements> elements) {
            return elements?.Select(e => (IOrbitalElementsSource)new MaterializedOrbitalElements(e));
        }

        private class MaterializedOrbitalElements : IOrbitalElementsSource {
            private readonly OrbitalElements elements;

            public MaterializedOrbitalElements(OrbitalElements elements) {
                this.elements = elements;
            }

            public string Name => elements.Name;
            public OrbitalElementsSourceEnum Source => elements.Source;

            public OrbitalElements ToOrbitalElements() => elements;
        }

        public async Task Load(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            try {
                progress?.Report(new ApplicationStatus() { Source = "Orbitals", Status = $"Loading Orbital Elements" });
                var tasks = new List<Task>();
                foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectTypeEnum)).Cast<OrbitalObjectTypeEnum>()) {
                    var loadObjectTypeTask = Task.Run(() => LoadObjectType(objectType, ct), ct);
                    tasks.Add(loadObjectTypeTask);
                }
                var jwstLoadTask = Task.Run(() => LoadJWST(ct), ct);
                tasks.Add(jwstLoadTask);

                await Task.WhenAll(tasks);
            } finally {
                progress?.Report(new ApplicationStatus() { Source = "Orbitals" });
                this.loadedEvent.Set();
            }
        }

        public void WaitUntilLoaded() {
            this.loadedEvent.WaitOne(-1);
        }

        private void UpdateBackend(OrbitalObjectTypeEnum objectType, OrbitalElementsBackend backend) {
            lock (backendLock) {
                if (backendsByType.TryGetValue(objectType, out var existingBackend)) {
                    existingBackend?.Dispose();
                }
                backendsByType[objectType] = backend;
            }
        }

        private void ClearBackend(OrbitalObjectTypeEnum objectType) {
            var newBackend = CreateDefaultBackend(objectType);
            lock (backendLock) {
                if (backendsByType.TryGetValue(objectType, out var existingBackend)) {
                    existingBackend?.Dispose();
                }
                backendsByType[objectType] = newBackend;
            }
            OnUpdated(objectType, newBackend);
        }

        public IEnumerable<OrbitalElements> Search(OrbitalObjectTypeEnum objectType, string searchString, int? limit = null) {
            try {
                var backend = GetBackend(objectType);
                return backend.Lookup.Query(searchString, limit);
            } catch (Exception e) {
                Logger.Error($"Failed to search using \"{searchString}\"", e);
                return Enumerable.Empty<OrbitalElements>();
            }
        }

        private OrbitalElementsBackend GetBackend(OrbitalObjectTypeEnum objectType) {
            lock (backendLock) {
                return backendsByType[objectType];
            }
        }

        public OrbitalElements Get(OrbitalObjectTypeEnum objectType, string objectName) {
            var backend = GetBackend(objectType);
            var ambiguous = false;

            var found = LookupOrNull(backend, objectName, ref ambiguous);
            if (found != null) {
                return found;
            }

            // A merge can append "(MPC)"/"(JPL)" to disambiguate a name carried by both
            // feeds. A sequence saved before that happened still references the bare name,
            // so try the suffixed forms before giving up. The bare lookup will usually have
            // reported an ambiguity on the way here, which is why nothing is surfaced until
            // every candidate has been tried.
            foreach (var suffix in DisambiguationSuffixes) {
                var ignored = false;
                found = LookupOrNull(backend, objectName + suffix, ref ignored);
                if (found != null) {
                    return found;
                }
            }

            if (ambiguous) {
                Logger.Error($"Multiple results found for {objectName}");
                Notification.ShowError($"Multiple results found for {objectName}");
            }
            return null;
        }

        private static readonly string[] DisambiguationSuffixes = new[] { " (MPC)", " (JPL)" };

        private static OrbitalElements LookupOrNull(
            OrbitalElementsBackend backend, string objectName, ref bool ambiguous) {
            try {
                return backend.Lookup.Lookup(objectName);
            } catch (DuplicateKeyException) {
                ambiguous = true;
                return null;
            }
        }

        public IReadOnlyDictionary<OrbitalElementsSourceEnum, OrbitalElementsFeedMetadata> GetFeedMetadata(OrbitalObjectTypeEnum objectType) {
            var result = new Dictionary<OrbitalElementsSourceEnum, OrbitalElementsFeedMetadata>();
            foreach (var source in GetContributingSources(objectType)) {
                var metadata = OrbitalElementsFeedMetadata.Load(GetFeedSavePath(objectType, source), source);
                if (metadata != null) {
                    result[source] = metadata;
                }
            }
            return result;
        }

        public DateTime GetLastUpdated(OrbitalObjectTypeEnum objectType) {
            var backend = GetBackend(objectType);
            return backend.LastModified;
        }

        public int GetCount(OrbitalObjectTypeEnum objectType) {
            var backend = GetBackend(objectType);
            return backend.Lookup.Count;
        }

        public Task Update(OrbitalObjectTypeEnum objectType, IEnumerable<IOrbitalElementsSource> elements, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var source = GetAccessor(objectType) == OrbitalElementsAccessorEnum.MPC
                ? OrbitalElementsSourceEnum.MPC
                : OrbitalElementsSourceEnum.JPL;
            return Update(objectType, source, elements, progress, ct);
        }

        /// <summary>
        /// Writes one feed store, then rebuilds the object type from whichever feeds the
        /// current source policy draws on. Taking the feed explicitly (rather than deriving
        /// it from the current option, as the old overload did) is what allows a "Both"
        /// update to write JPL and MPC independently.
        /// </summary>
        public Task Update(OrbitalObjectTypeEnum objectType, OrbitalElementsSourceEnum source, IEnumerable<IOrbitalElementsSource> elements, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Update(objectType, source, elements, importedFrom: null, progress: progress, ct: ct);
        }

        public Task Update(OrbitalObjectTypeEnum objectType, OrbitalElementsSourceEnum source, IEnumerable<IOrbitalElementsSource> elements, string importedFrom, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.Run(() => {
                var path = GetFeedSavePath(objectType, source);
                var tmpPath = path + ".temp";
                try {
                    progress?.Report(new ApplicationStatus() {
                        Status = $"Updating {objectType.ToDescriptionString()} Elements"
                    });
                    // OrbitalsPlugin's ImportingConstructor creates OrbitalElementsDirectory
                    // at plugin load, but defend against the directory being deleted at
                    // runtime (e.g. user troubleshooting) so Update doesn't silently fail.
                    Directory.CreateDirectory(Path.GetDirectoryName(tmpPath));
                    var written = 0;
                    using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var gs = new GZipStream(fs, CompressionLevel.Optimal)) {
                        foreach (var element in elements) {
                            ct.ThrowIfCancellationRequested();
                            var converted = element.ToOrbitalElements();
                            if (converted.Source == OrbitalElementsSourceEnum.Unknown) {
                                converted.Source = source;
                            }
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<OrbitalElements>(gs, converted, ProtoBuf.PrefixStyle.Base128, 1);
                            written++;
                        }
                    }
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                    File.Move(tmpPath, path);
                    OrbitalElementsFeedMetadata.Save(path, new OrbitalElementsFeedMetadata() {
                        Source = source,
                        Imported = importedFrom != null,
                        AcquiredAt = DateTime.Now,
                        SourceFileName = importedFrom,
                        RecordCount = written
                    });

                    // Rebuild from disk so the in-memory view reflects the current policy --
                    // under "Both" this feed is only half of the answer.
                    LoadObjectType(objectType, ct);
                } catch (OperationCanceledException) {
                    Logger.Warning($"Updating {objectType} orbital elements cancelled");
                    return;
                } catch (Exception e) {
                    Logger.Error($"Failed to update {objectType} orbital elements", e);
                    Notification.ShowError($"Failed to update {objectType.ToDescriptionString()} orbital elements. {e.Message}");
                } finally {
                    progress?.Report(new ApplicationStatus());
                }
            });
        }

        private void OnUpdated(OrbitalObjectTypeEnum objectType, OrbitalElementsBackend backend) {
            OnUpdated(objectType, backend, null);
        }

        private void OnUpdated(
            OrbitalObjectTypeEnum objectType,
            OrbitalElementsBackend backend,
            IReadOnlyDictionary<OrbitalElementsSourceEnum, OrbitalElementsFeedMetadata> feeds) {
            this.Updated?.Invoke(this, new OrbitalElementsObjectTypeUpdatedEventArgs() {
                ObjectType = objectType,
                Count = backend.Lookup.Count,
                LastUpdated = backend.LastModified,
                Feeds = feeds
            });
        }

        private void OnVectorTableUpdated(PVTable pvTable) {
            var validUntilJd = pvTable.Rows.LastOrDefault()?.Epoch_jd;
            this.VectorTableUpdated?.Invoke(this, new VectorTableUpdatedEventArgs() {
                Name = pvTable.Name,
                ValidUntil = validUntilJd.HasValue ? NOVAS.JulianToDateTime(validUntilJd.Value) : DateTime.MinValue
            });
        }

        public OrbitalPositionVelocity GetSolarSystemBodyPV(
            DateTime asof, SolarSystemBody solarSystemBody, Angle latitude, Angle longitude, double elevation, TimeSpan rateDriftDelta) {
            var startResult = AstrometricTopocentric(asof, solarSystemBody, latitude, longitude, elevation);
            var nextResult = AstrometricTopocentric(asof + rateDriftDelta, solarSystemBody, latitude, longitude, elevation);

            var trackingRate = SiderealShiftTrackingRate.Create(startResult.Coords, nextResult.Coords, rateDriftDelta);
            return new OrbitalPositionVelocity(asof, startResult.Vector, null, startResult.Coords, trackingRate);
        }

        /// <summary>
        /// Astrometric topocentric place of a major solar system body, via NOVAS place().
        ///
        /// "Topocentric" is the important part: NOVAS app_planet (what
        /// <see cref="NOVAS.PlanetApparentCoordinates"/> wraps) produces a strictly
        /// GEOCENTRIC place, which for the Moon is wrong by up to ~1 degree of diurnal
        /// parallax -- more than enough to put the target outside the FOV. Passing an
        /// on-surface observer to place() applies the parallax, and also makes the
        /// differenced tracking rate pick up the parallax rate (~240 arcsec/hr in RA for
        /// the Moon).
        ///
        /// "Astrometric" (light-time corrected, but no aberration, deflection, or
        /// precession/nutation) is chosen to match what <see cref="GetObjectPV"/> returns
        /// for asteroids and comets, and because it is what NINA expects: NINA's
        /// Coordinates.Transform(Epoch.JNOW) applies SOFA Atci13, which adds aberration,
        /// light deflection, and precession/nutation on top of an ICRS/J2000 input.
        ///
        /// Verified against JPL Horizons "R.A.___(ICRF)___DEC" for a topocentric center:
        /// agreement is better than 0.05 arcsec for the Moon, Sun, Venus, Mars, Jupiter
        /// and Saturn.
        /// </summary>
        private (RectangularCoordinates Vector, Coordinates Coords) AstrometricTopocentric(
            DateTime asof, SolarSystemBody solarSystemBody, Angle latitude, Angle longitude, double elevation) {
            var celestialObject = new NOVAS.CelestialObject() {
                Type = (short)NOVAS.ObjectType.MajorPlanetSunOrMoon,
                Number = (short)solarSystemBody,
                Name = solarSystemBody.ToString(),
                Star = default
            };
            var observer = new NOVAS.Observer() {
                Where = (short)NOVAS.ObserverLocation.EarthSurface,
                OnSurf = new NOVAS.OnSurface() {
                    Latitude = latitude.Degree,
                    Longitude = longitude.Degree,
                    Height = elevation
                }
            };

            var skyPosition = default(NOVAS.SkyPosition);
            // place() wants a TT julian date, and delta-T separately so it can recover UT1
            // for the Earth-rotation part of the observer's geocentric position.
            var result = NOVAS.Place(
                AstroUtilCompat.GetJulianDateTT(asof), celestialObject, observer, AstroUtil.DeltaT(asof),
                NOVAS.CoordinateSystem.Astrometric, NOVAS.Accuracy.Full, ref skyPosition);
            if (result != 0) {
                throw new Exception($"NOVAS place failed for {solarSystemBody}. Result={result}");
            }

            var coordinates = new Coordinates(Angle.ByHours(skyPosition.RA), Angle.ByDegree(skyPosition.Dec), Epoch.J2000);

            // Rebuild the topocentric vector from RA/Dec/distance rather than reading
            // SkyPosition.RHat: RHat is a ByValArray field that is null going in, so it is
            // not dependable across marshalling. Only Distance is consumed downstream
            // (OrbitalsVM, OrbitalsContainerBase), and this keeps the frame consistent with
            // the vector GetObjectPV returns for asteroids.
            var ra = coordinates.RA * Math.PI / 12.0;
            var dec = AstroUtil.ToRadians(coordinates.Dec);
            var vector = new RectangularCoordinates(
                skyPosition.Dis * Math.Cos(dec) * Math.Cos(ra),
                skyPosition.Dis * Math.Cos(dec) * Math.Sin(ra),
                skyPosition.Dis * Math.Sin(dec));
            return (vector, coordinates);
        }

        public OrbitalPositionVelocity GetObjectPV(DateTime asof, OrbitalElements orbitalElements, Angle latitude, Angle longitude, double elevation, TimeSpan rateDriftDelta) {
            var observerJdtt = AstroUtilCompat.GetJulianDateTT(asof);
            var startResult = ApparentTopocentricWithLightTime(observerJdtt, orbitalElements, latitude, longitude, elevation);

            var nextObserverJdtt = observerJdtt + AstrometricConstants.JD_SEC * rateDriftDelta.TotalSeconds;
            var nextResult = ApparentTopocentricWithLightTime(nextObserverJdtt, orbitalElements, latitude, longitude, elevation);

            var trackingRate = SiderealShiftTrackingRate.Create(startResult.Coords, nextResult.Coords, rateDriftDelta);
            return new OrbitalPositionVelocity(asof, startResult.Vector, null, startResult.Coords, trackingRate);
        }

        /// <summary>
        /// Apparent topocentric position with single-iteration light-time correction:
        /// the position you see at <paramref name="observerJdtt"/> is the object's
        /// geometric position at observerJdtt - distance/c. Earth and observer
        /// positions are taken at observerJdtt (when the light arrives), the object
        /// position is taken at the earlier emit time (when the light departed).
        /// One iteration is sufficient for solar-system distances; the residual is
        /// O((v_object * lt / c)^2), sub-arcsec at any practical distance.
        /// </summary>
        private (RectangularCoordinates Vector, Coordinates Coords) ApparentTopocentricWithLightTime(
            double observerJdtt, OrbitalElements orbitalElements,
            Angle latitude, Angle longitude, double elevation) {
            // Initial geometric position at observerJdtt -- used only to estimate distance.
            var instantaneousPosition = Kepler.CalculateOrbitalElements(orbitalElements, observerJdtt);
            var instantaneousTopocentric = Kepler.GetTopocentricJ2000Position(
                instantaneousPosition, observerJdtt, NOVAS.Body.Earth, latitude, longitude, elevation);
            var distance_au = Math.Sqrt(instantaneousTopocentric.X * instantaneousTopocentric.X
                                      + instantaneousTopocentric.Y * instantaneousTopocentric.Y
                                      + instantaneousTopocentric.Z * instantaneousTopocentric.Z);
            var lightTime_days = distance_au / AstrometricConstants.SPEED_OF_LIGHT_AU_PER_DAY;

            // Re-propagate the object back by the light-travel time; Earth and observer
            // stay at observerJdtt. The overload accepting observerJdtt does exactly that.
            var emitPosition = Kepler.CalculateOrbitalElements(orbitalElements, observerJdtt - lightTime_days);
            var topocentric = Kepler.GetTopocentricJ2000Position(
                emitPosition, observerJdtt, NOVAS.Body.Earth, latitude, longitude, elevation);
            return (topocentric, topocentric.ToPolar());
        }

        /// <summary>
        /// Path to one feed store. The MPC_ prefix predates this change and is preserved so
        /// existing caches keep working; JPL keeps the unprefixed name it has always used.
        /// The two coexist, which is what lets "Both" merge them.
        /// </summary>
        private static string GetFeedSavePath(OrbitalObjectTypeEnum objectType, OrbitalElementsSourceEnum source) {
            var prefix = source == OrbitalElementsSourceEnum.MPC ? "MPC_" : "";
            return Path.Combine(OrbitalsPlugin.OrbitalElementsDirectory, $"{prefix}{objectType}Elements.bin.gz");
        }

        private static string GetJWSTSavePath() {
            return Path.Combine(OrbitalsPlugin.OrbitalElementsDirectory, $"JWSTVectorTable.bin.gz");
        }

        public event EventHandler<OrbitalElementsObjectTypeUpdatedEventArgs> Updated;

        public event EventHandler<VectorTableUpdatedEventArgs> VectorTableUpdated;

        public Task UpdateJWST(PVTable pvTable, IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.Run(() => {
                var path = GetJWSTSavePath();
                var tmpPath = path + ".temp";
                try {
                    ct.ThrowIfCancellationRequested();

                    progress?.Report(new ApplicationStatus() {
                        Status = $"Updating JWST Vector Table"
                    });
                    // Same defensive check as Update -- see comment there.
                    Directory.CreateDirectory(Path.GetDirectoryName(tmpPath));
                    using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var gs = new GZipStream(fs, CompressionLevel.Optimal)) {
                        ProtoBuf.Serializer.SerializeWithLengthPrefix<PVTable>(gs, pvTable, ProtoBuf.PrefixStyle.Base128, 1);
                    }
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                    File.Move(tmpPath, path);
                    this.jwstVectorTable = pvTable;
                    OnVectorTableUpdated(pvTable);
                } catch (OperationCanceledException) {
                    Logger.Warning($"Updating JWST vector table cancelled");
                    return;
                } catch (Exception e) {
                    Logger.Error($"Failed to update JWST vector table", e);
                    Notification.ShowError($"Failed to update JWST vector table. {e.Message}");
                } finally {
                    progress?.Report(new ApplicationStatus());
                }
            }, ct);
        }

        public DateTime GetJWSTValidUntil() {
            var vectorTable = jwstVectorTable;
            if (vectorTable == null) {
                return DateTime.MinValue;
            }
            var validUntilJd = vectorTable.Rows.LastOrDefault()?.Epoch_jd;
            return validUntilJd.HasValue ? NOVAS.JulianToDateTime(validUntilJd.Value) : DateTime.MinValue;
        }

        public PVTable GetJWSTVectorTable() {
            return jwstVectorTable;
        }

        public OrbitalPositionVelocity GetPVFromTable(
            DateTime asof, PVTable vectorTable, Angle latitude, Angle longitude, double elevation, TimeSpan rateDriftDelta) {
            if (vectorTable == null || vectorTable.Rows.Count <= 1) {
                return null;
            }

            var startJd = vectorTable.Rows.First().Epoch_jd;
            var endJd = vectorTable.Rows.Last().Epoch_jd;
            var asofJd = AstroUtilCompat.GetJulianDateTT(asof);
            if (asofJd < startJd) {
                Logger.Trace($"No vector data available for JWST at {asof}. The earliest available is {NOVAS.JulianToDateTime(startJd)}");
                return null;
            } else if (asofJd >= endJd) {
                Logger.Trace($"No vector data available for JWST at {asof}. The last available is {NOVAS.JulianToDateTime(endJd)}");
                return null;
            }

            var foundIndex = vectorTable.Rows.BinarySearchWithKey(r => r.Epoch_jd, asofJd);
            var mostRecentPV = vectorTable.Rows[foundIndex];
            var nextPV = vectorTable.Rows[foundIndex + 1];
            var daysSinceMostRecentEpoch = asofJd - mostRecentPV.Epoch_jd;

            // Interpolate between the most recent position + velocity, and the next one. To improve accuracy, we assume constant acceleration since velocity changes
            // between each entry. This isn't completely correct, but it will not be far off since jerk for JWST is low by design
            var mostRecentPosition = mostRecentPV.GetPosition();
            var mostRecentVelocity = mostRecentPV.GetVelocity();
            var nextVelocity = nextPV.GetVelocity();
            var acceleration = (nextVelocity - mostRecentVelocity) / (nextPV.Epoch_jd - mostRecentPV.Epoch_jd);

            // v = at + v0
            // d = 1/2 * at^2 + v0 * t + d0
            var startEarthPV = GetPVOnEarthSurface(asof, latitude, longitude, elevation);
            var startApparentGeocentricPosition = mostRecentPosition + mostRecentVelocity * daysSinceMostRecentEpoch + acceleration * daysSinceMostRecentEpoch * daysSinceMostRecentEpoch * 0.5;
            var startApparentPosition = startApparentGeocentricPosition - startEarthPV.Position.RotateEcliptic(-AstrometricConstants.J2000MeanObliquity);

            daysSinceMostRecentEpoch += AstrometricConstants.JD_SEC;
            var nextEarthPV = GetPVOnEarthSurface(asof + rateDriftDelta, latitude, longitude, elevation);
            var nextApparentGeocentricPosition = mostRecentPosition + mostRecentVelocity * daysSinceMostRecentEpoch + acceleration * daysSinceMostRecentEpoch * daysSinceMostRecentEpoch * 0.5;
            var nextApparentPosition = nextApparentGeocentricPosition - nextEarthPV.Position.RotateEcliptic(-AstrometricConstants.J2000MeanObliquity);

            var startCoordinates = startApparentPosition.RotateEcliptic(AstrometricConstants.J2000MeanObliquity).ToPolar();
            var nextCoordinates = nextApparentPosition.RotateEcliptic(AstrometricConstants.J2000MeanObliquity).ToPolar();
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates, nextCoordinates, rateDriftDelta);
            return new OrbitalPositionVelocity(asof, startApparentGeocentricPosition, null, startCoordinates, trackingRate);
        }

        public void Clear(OrbitalObjectTypeEnum objectType) {
            try {
                var cleared = false;
                foreach (var source in GetContributingSources(objectType)) {
                    var path = GetFeedSavePath(objectType, source);
                    if (File.Exists(path)) {
                        File.Delete(path);
                        cleared = true;
                    }
                    OrbitalElementsFeedMetadata.Delete(path);
                }
                if (cleared) {
                    ClearBackend(objectType);
                }
            } catch (Exception e) {
                Logger.Error($"Failed to clear {objectType}", e);
                Notification.ShowError($"Failed to clear {objectType}. {e.Message}");
            }
        }
    }
}