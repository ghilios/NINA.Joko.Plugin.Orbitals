﻿#region "copyright"

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
                var lookup = new TrigramStringMap<OrbitalElements>();
                lookup.AddRange(o => o.Name, objects);
                return new OrbitalElementsBackend(objectType, lastModified, lookup);
            }

            public void Dispose() {
                Lookup?.Dispose();
            }
        }

        private readonly object backendLock = new object();
        private readonly Dictionary<OrbitalObjectTypeEnum, OrbitalElementsBackend> backendsByType = new Dictionary<OrbitalObjectTypeEnum, OrbitalElementsBackend>();

        public OrbitalElementsAccessor() {
            foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectTypeEnum)).Cast<OrbitalObjectTypeEnum>()) {
                backendsByType.Add(objectType, OrbitalElementsBackend.Create(objectType, DateTime.MinValue, Enumerable.Empty<OrbitalElements>(), CancellationToken.None));
            }
        }

        public async Task Load(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            try {
                progress?.Report(new ApplicationStatus() { Source = "Orbitals", Status = $"Loading Orbital Elements" });
                var tasks = new List<Task>();
                foreach (var objectType in Enum.GetValues(typeof(OrbitalObjectTypeEnum)).Cast<OrbitalObjectTypeEnum>()) {
                    var loadObjectTypeTask = Task.Run(() => {
                        var path = GetObjectTypeSavePath(objectType);
                        if (!File.Exists(path)) {
                            Logger.Info($"No {objectType} orbital elements loaded since no file was found at {path}");
                            return;
                        }

                        try {
                            ct.ThrowIfCancellationRequested();
                            var lastModified = File.GetLastWriteTime(path);
                            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var gs = new GZipStream(fs, CompressionMode.Decompress)) {
                                var orbitalElements = ProtoBuf.Serializer.DeserializeItems<OrbitalElements>(gs, ProtoBuf.PrefixStyle.Base128, 1);
                                var backend = OrbitalElementsBackend.Create(objectType, lastModified, orbitalElements, ct);
                                UpdateBackend(objectType, backend);
                                OnUpdated(objectType, backend);
                            }
                        } catch (OperationCanceledException) {
                            return;
                        } catch (Exception e) {
                            Logger.Error($"Failed to load {objectType} orbital elements", e);
                            Notification.ShowError($"Failed to load {objectType} orbital elements");
                        }
                    }, ct);
                    tasks.Add(loadObjectTypeTask);
                }

                await Task.WhenAll(tasks);
            } finally {
                progress?.Report(new ApplicationStatus() { Source = "Orbitals" });
            }
        }

        private void UpdateBackend(OrbitalObjectTypeEnum objectType, OrbitalElementsBackend backend) {
            lock (backendLock) {
                if (backendsByType.TryGetValue(objectType, out var existingBackend)) {
                    existingBackend?.Dispose();
                }
                backendsByType[objectType] = backend;
            }
        }

        public IEnumerable<OrbitalElements> Search(OrbitalObjectTypeEnum objectType, string searchString, int? limit = null) {
            var backend = GetBackend(objectType);
            return backend.Lookup.Query(searchString, limit);
        }

        private OrbitalElementsBackend GetBackend(OrbitalObjectTypeEnum objectType) {
            lock (backendLock) {
                return backendsByType[objectType];
            }
        }

        public OrbitalElements Get(OrbitalObjectTypeEnum objectType, string objectName) {
            try {
                var backend = GetBackend(objectType);
                return backend.Lookup.Lookup(objectName);
            } catch (DuplicateKeyException) {
                Logger.Error($"Multiple results found for {objectName}");
                Notification.ShowError($"Multiple results found for {objectName}");
                return null;
            }
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
            return Task.Run(() => {
                var path = GetObjectTypeSavePath(objectType);
                var tmpPath = path + ".temp";
                try {
                    progress?.Report(new ApplicationStatus() {
                        Status = $"Updating {objectType.ToDescriptionString()} Elements"
                    });
                    var backend = OrbitalElementsBackend.Create(objectType, DateTime.Now, elements.Select(e => e.ToOrbitalElements()), ct);
                    using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var gs = new GZipStream(fs, CompressionLevel.Optimal)) {
                        foreach (var element in backend.Lookup) {
                            ct.ThrowIfCancellationRequested();
                            ProtoBuf.Serializer.SerializeWithLengthPrefix<OrbitalElements>(gs, element, ProtoBuf.PrefixStyle.Base128, 1);
                        }
                    }
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                    File.Move(tmpPath, path);
                    UpdateBackend(objectType, backend);
                    OnUpdated(objectType, backend);
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
            this.Updated?.Invoke(this, new OrbitalElementsObjectTypeUpdatedEventArgs() {
                ObjectType = objectType,
                Count = backend.Lookup.Count,
                LastUpdated = backend.LastModified
            });
        }

        public OrbitalPositionVelocity GetSolarSystemBodyPV(DateTime asof, SolarSystemBody solarSystemBody) {
            var jdtt = AstroUtil.GetJulianDate(asof);
            var startPosition = NOVAS.BodyPositionAndVelocity(jdtt, solarSystemBody.ToNOVAS(), NOVAS.SolarSystemOrigin.SolarCenterOfMass);
            var earthPosition = NOVAS.BodyPositionAndVelocity(jdtt, NOVAS.Body.Earth, NOVAS.SolarSystemOrigin.SolarCenterOfMass);
            var earthCenteredPosition = startPosition.Position - earthPosition.Position;
            var startCoordinates = NOVAS.PlanetApparentCoordinates(jdtt, solarSystemBody.ToNOVAS());
            var nextCoordinates = NOVAS.PlanetApparentCoordinates(jdtt + AstrometricConstants.JD_SEC, solarSystemBody.ToNOVAS());
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates, nextCoordinates, TimeSpan.FromSeconds(1));
            return new OrbitalPositionVelocity(asof, earthCenteredPosition, startCoordinates, trackingRate);
        }

        public OrbitalPositionVelocity GetObjectPV(DateTime asof, OrbitalElements orbitalElements) {
            var jdtt = AstroUtil.GetJulianDate(asof);
            var startPosition = Kepler.CalculateOrbitalElements(orbitalElements, jdtt);
            var nextPosition = Kepler.CalculateOrbitalElements(orbitalElements, jdtt + AstrometricConstants.JD_SEC);
            var startApparentPosition = Kepler.GetApparentPosition(startPosition, NOVAS.Body.Earth);
            var startCoordinates = startApparentPosition.ToPolar();
            var nextApparentPosition = Kepler.GetApparentPosition(nextPosition, NOVAS.Body.Earth);
            var nextCoordinates = nextApparentPosition.ToPolar();
            var trackingRate = SiderealShiftTrackingRate.Create(startCoordinates, nextCoordinates, TimeSpan.FromSeconds(1));
            return new OrbitalPositionVelocity(asof, startApparentPosition, startCoordinates, trackingRate);
        }

        private static string GetObjectTypeSavePath(OrbitalObjectTypeEnum objectType) {
            return Path.Combine(OrbitalsPlugin.OrbitalElementsDirectory, $"{objectType}Elements.bin.gz");
        }

        public event EventHandler<OrbitalElementsObjectTypeUpdatedEventArgs> Updated;
    }
}