using Gma.DataStructures.StringSearch;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    public class OrbitalElementsAccessor : IOrbitalElementsAccessor {
        private class OrbitalElementsBackend {
            private OrbitalElementsBackend(OrbitalObjectType objectType, DateTime lastModified, ImmutableList<OrbitalElements> objects, SuffixTrie<OrbitalElements> lookup) {
                this.ObjectType = objectType;
                this.Objects = objects;
                this.Lookup = lookup;
                this.LastModified = lastModified;
            }

            public OrbitalObjectType ObjectType { get; private set; }
            public ImmutableList<OrbitalElements> Objects { get; private set; }
            public SuffixTrie<OrbitalElements> Lookup { get; private set; }
            public DateTime LastModified { get; private set; }

            public static OrbitalElementsBackend Create(OrbitalObjectType objectType, DateTime lastModified, IEnumerable<OrbitalElements> objects, CancellationToken ct) {
                var objectsBuilder = ImmutableList.CreateBuilder<OrbitalElements>();
                var lookup = new SuffixTrie<OrbitalElements>(3);
                foreach (var o in objects) {
                    ct.ThrowIfCancellationRequested();
                    objectsBuilder.Add(o);
                    lookup.Add(o.Name.ToLowerInvariant(), o);
                }
                return new OrbitalElementsBackend(objectType, lastModified, objectsBuilder.ToImmutable(), lookup);
            }
        }

        private readonly object backendLock = new object();
        private readonly Dictionary<OrbitalObjectType, OrbitalElementsBackend> backendsByType = new Dictionary<OrbitalObjectType, OrbitalElementsBackend>();

        public OrbitalElementsAccessor() {
            foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectType)).Cast<OrbitalObjectType>()) {
                backendsByType.Add(objectType, OrbitalElementsBackend.Create(objectType, DateTime.MinValue, Enumerable.Empty<OrbitalElements>(), CancellationToken.None));
            }
        }

        public Task Load(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            return Task.Run(() => {
                try {
                    foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectType)).Cast<OrbitalObjectType>()) {
                        var path = GetObjectTypeSavePath(objectType);
                        if (!File.Exists(path)) {
                            Logger.Info($"No {objectType} orbital elements loaded since no file was found at {path}");
                        }

                        progress.Report(new ApplicationStatus() { Source = "Orbitals", Status = $"Loading {objectType}" });
                        try {
                            ct.ThrowIfCancellationRequested();
                            var lastModified = File.GetLastWriteTime(path);
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                                var orbitalElements = ProtoBuf.Serializer.DeserializeItems<OrbitalElements>(fs, ProtoBuf.PrefixStyle.Base128, 1);
                                var backend = OrbitalElementsBackend.Create(objectType, lastModified, orbitalElements, ct);
                                lock (backendLock) {
                                    backendsByType[objectType] = backend;
                                }
                            }
                        } catch (OperationCanceledException) {
                            return;
                        } catch (Exception e) {
                            Logger.Error($"Failed to load {objectType} orbital elements", e);
                            Notification.ShowError($"Failed to load {objectType} orbital elements");
                        }
                    }
                } finally {
                    progress.Report(new ApplicationStatus() { Source = "Orbitals" });
                }
            });
        }

        public IEnumerable<OrbitalElements> Search(OrbitalObjectType objectType, string searchString) {
            var backend = GetBackend(objectType);
            return backend.Lookup.Retrieve(searchString.ToLowerInvariant());
        }

        private OrbitalElementsBackend GetBackend(OrbitalObjectType objectType) {
            lock (backendLock) {
                return backendsByType[objectType];
            }
        }

        public OrbitalElements Get(OrbitalObjectType objectType, string objectName) {
            var searchResults = Search(objectType, objectName);
            try {
                return searchResults.SingleOrDefault();
            } catch (InvalidOperationException) {
                Logger.Error($"Multiple results found for {objectName}");
                Notification.ShowError($"Multiple results found for {objectName}");
                return null;
            }
        }

        public DateTime GetLastUpdated(OrbitalObjectType objectType) {
            var backend = GetBackend(objectType);
            return backend.LastModified;
        }

        public int GetCount(OrbitalObjectType objectType) {
            var backend = GetBackend(objectType);
            return backend.Objects.Count;
        }

        public Task Update(OrbitalObjectType objectType, IEnumerable<IOrbitalElementsSource> elements, CancellationToken ct) {
            return Task.Run(() => {
                var path = GetObjectTypeSavePath(objectType);
                var tmpPath = path + ".temp";
                try {
                    var backend = OrbitalElementsBackend.Create(objectType, DateTime.UtcNow, elements.Select(e => e.ToOrbitalElements()), ct);
                    using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        foreach (var element in backend.Objects) {
                            ct.ThrowIfCancellationRequested();
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<OrbitalElements>(fs, element, ProtoBuf.PrefixStyle.Base128, 1);
                        }
                    }
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                    File.Move(tmpPath, path);
                    this.Updated?.Invoke(this, new OrbitalElementsObjectTypeUpdatedEventArgs() {
                        ObjectType = objectType,
                        Count = backend.Objects.Count,
                        LastUpdated = backend.LastModified
                    });
                } catch (OperationCanceledException) {
                    Logger.Warning($"Updating {objectType} orbital elements cancelled");
                    return;
                } catch (Exception e) {
                    Logger.Error($"Failed to update {objectType} orbital elements", e);
                    Notification.ShowError($"Failed to update {objectType} orbital elements");
                }
            });
        }

        public OrbitalPositionVelocity GetSolarSystemBodyPV(DateTime asof, SolarSystemBody solarSystemBody) {
            var jdtt = AstroUtil.GetJulianDate(asof);
            var startCoordinates = NOVAS.PlanetApparentCoordinates(jdtt, solarSystemBody.ToNOVAS());
            var nextCoordinates = NOVAS.PlanetApparentCoordinates(jdtt + AstrometricConstants.JD_SEC, solarSystemBody.ToNOVAS());
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates, nextCoordinates, TimeSpan.FromSeconds(1));
            return new OrbitalPositionVelocity(asof, startCoordinates, trackingRate);
        }

        public OrbitalPositionVelocity GetObjectPV(DateTime asof, OrbitalElements orbitalElements) {
            var jdtt = AstroUtil.GetJulianDate(asof);
            var startPosition = Kepler.CalculateOrbitalElements(orbitalElements, jdtt);
            var nextPosition = Kepler.CalculateOrbitalElements(orbitalElements, jdtt + AstrometricConstants.JD_SEC);
            var startCoordinates = Kepler.GetApparentCoordinates(startPosition, NOVAS.Body.Earth);
            var nextCoordinates = Kepler.GetApparentCoordinates(nextPosition, NOVAS.Body.Earth);
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates, nextCoordinates, TimeSpan.FromSeconds(1));
            return new OrbitalPositionVelocity(asof, startCoordinates, trackingRate);
        }

        private static string GetObjectTypeSavePath(OrbitalObjectType objectType) {
            return Path.Combine(OrbitalsPlugin.OrbitalElementsDirectory, $"{objectType}Elements.bin");
        }

        public event EventHandler<OrbitalElementsObjectTypeUpdatedEventArgs> Updated;
    }
}
