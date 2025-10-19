#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    public abstract class OrbitalsContainerBase<T> : SequenceContainer, IDeepSkyObjectContainer where T : OrbitalsObjectBase {
        protected readonly IProfileService profileService;
        protected readonly INighttimeCalculator nighttimeCalculator;
        protected readonly IOrbitalsOptions orbitalsOptions;
        private InputTarget target;

        private Task coordinateUpdateTask;
        private CancellationTokenSource coordinateUpdateCts;

        public OrbitalsContainerBase(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IOrbitalsOptions orbitalsOptions) : base(new SequentialStrategy()) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.orbitalsOptions = orbitalsOptions;
            _ = Task.Run(() => NighttimeData = nighttimeCalculator.Calculate());
        }

        protected void PostConstruction() {
            OffsetCoordinates = new InputCoordinatesEx();

            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.LocationChanged), ProfileService_LocationChanged);
            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.HorizonChanged), ProfileService_HorizonChanged);
        }

        private bool offsetExpanded = false;

        [JsonProperty]
        public bool OffsetExpanded {
            get => offsetExpanded;
            set {
                if (offsetExpanded != value) {
                    offsetExpanded = value;
                    RaisePropertyChanged();
                }
            }
        }

        private InputCoordinatesEx offsetCoordinates;

        [JsonProperty]
        public InputCoordinatesEx OffsetCoordinates {
            get => offsetCoordinates;
            set {
                if (offsetCoordinates != null) {
                    offsetCoordinates.CoordinatesChanged -= OffsetCoordinates_OnCoordinatesChanged;
                }
                offsetCoordinates = value;
                if (offsetCoordinates != null) {
                    offsetCoordinates.CoordinatesChanged += OffsetCoordinates_OnCoordinatesChanged;
                }
                RaiseOffsetChanged();
            }
        }

        private bool deserializing = false;

        [OnDeserializing]
        public void OnOrbitalsDeserializing(StreamingContext context) {
            deserializing = true;
        }

        [OnDeserialized]
        public void OnOrbitalsDeserialized(StreamingContext context) {
            deserializing = false;
            RaiseOffsetChanged();
        }

        private void OffsetCoordinates_OnCoordinatesChanged(object sender, EventArgs e) {
            RaiseOffsetChanged();
        }

        private void RaiseOffsetChanged() {
            if (!deserializing) {
                RaisePropertyChanged(nameof(OffsetCoordinates));
                RefreshCoordinates();
            }
        }

        private async Task CoordinateUpdateLoop(CancellationToken ct) {
            Logger.Info($"Entering coordinate update loop for {this.Name}");
            try {
                while (!ct.IsCancellationRequested) {
                    RefreshCoordinates();
                    await AfterRefreshCoordinates(ct);
                    await Task.Delay(TimeSpan.FromSeconds(this.GetCoordinateRefreshTime()), ct);
                }
            } finally {
                Logger.Info($"Exited coordinate update loop for {this.Name}");
            }
        }

        protected virtual int GetCoordinateRefreshTime() {
            return this.orbitalsOptions.OrbitalPositionRefreshTime_sec;
        }

        public T TargetObject => (T)Target.DeepSkyObject;

        protected void RefreshCoordinates() {
            try {
                var targetPosition = TargetObject.PositionAt(DateTime.UtcNow);
                var targetCoordinates = targetPosition.Coordinates;
                if (OffsetCoordinates != null) {
                    var newDec = targetCoordinates.Dec + offsetCoordinates.Coordinates.Dec;
                    var newRa = targetCoordinates.RA + offsetCoordinates.Coordinates.RA;
                    if (newDec < -90.0 || newDec > 90.0) {
                        Notification.ShowWarning("Invalid dec after applying offset. Resetting offset.");
                        OffsetCoordinates.Coordinates = new Coordinates(Angle.Zero, Angle.Zero, Epoch.J2000);
                    } else {
                        newRa = AstroUtil.EuclidianModulus(newRa, 24.0);
                        targetCoordinates.Dec = newDec;
                        targetCoordinates.RA = newRa;
                    }
                }

                Position = targetPosition;
                Target.InputCoordinates.Coordinates = targetCoordinates;
                ShiftTrackingRate = Target.DeepSkyObject.ShiftTrackingRate;
                Distance.AU = TargetObject.Position.Distance;
                AfterParentChanged();
            } catch (Exception e) {
                Logger.Error("Error while refreshing coordinates", e);
            }
        }

        protected virtual Task AfterRefreshCoordinates(CancellationToken ct) {
            return Task.CompletedTask;
        }

        public override void Teardown() {
            base.Teardown();

            try {
                coordinateUpdateCts?.Cancel();
            } finally {
                coordinateUpdateCts = null;
                coordinateUpdateTask = null;
            }
        }

        public override void Initialize() {
            base.Initialize();

            AfterParentChanged();
        }

        private SiderealShiftTrackingRate shiftTrackingRate = SiderealShiftTrackingRate.Disabled;

        public SiderealShiftTrackingRate ShiftTrackingRate {
            get => shiftTrackingRate;
            private set {
                shiftTrackingRate = value;
                RaisePropertyChanged();
            }
        }

        private Distance distance = new Distance(0.0d);

        public Distance Distance => distance;

        private OrbitalPositionVelocity position;

        public OrbitalPositionVelocity Position {
            get => position;
            private set {
                position = value;
                RaisePropertyChanged();
            }
        }

        private bool invalid = false;

        public bool Invalid {
            get => invalid;
            private set {
                if (invalid != value) {
                    invalid = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void ProfileService_HorizonChanged(object sender, EventArgs e) {
            Target?.DeepSkyObject?.SetCustomHorizon(profileService.ActiveProfile.AstrometrySettings.Horizon);
        }

        private void ProfileService_LocationChanged(object sender, EventArgs e) {
            Target?.SetPosition(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude));
        }

        public NighttimeData NighttimeData { get; private set; }

        [JsonProperty]
        public InputTarget Target {
            get => target;
            set {
                if (Target != null) {
                    WeakEventManager<InputTarget, EventArgs>.RemoveHandler(Target, nameof(Target.CoordinatesChanged), Target_OnCoordinatesChanged);
                }
                target = value;
                if (Target != null) {
                    WeakEventManager<InputTarget, EventArgs>.AddHandler(Target, nameof(Target.CoordinatesChanged), Target_OnCoordinatesChanged);
                }
                RaisePropertyChanged();
            }
        }

        private void Target_OnCoordinatesChanged(object sender, EventArgs e) {
            AfterParentChanged();
        }

        public override string ToString() {
            var baseString = base.ToString();
            return $"{baseString}, Target: {Target?.TargetName} {Target?.DeepSkyObject?.Coordinates} {Target?.PositionAngle}";
        }

        public override bool Validate() {
            if (Target.InputCoordinates?.Coordinates == null
                || Target.InputCoordinates.Coordinates.RA == 0.0d
                || Target.InputCoordinates.Coordinates.Dec == 0.0d) {
                Invalid = true;
            } else {
                Invalid = false;
            }
            return base.Validate();
        }

        public override void AfterParentChanged() {
            if (Parent != null) {
                if (coordinateUpdateTask == null) {
                    coordinateUpdateCts = new CancellationTokenSource();
                    coordinateUpdateTask = Task.Run(() => CoordinateUpdateLoop(coordinateUpdateCts.Token));
                }
            } else {
                if (coordinateUpdateTask != null) {
                    coordinateUpdateCts?.Cancel();
                    coordinateUpdateCts = null;
                    coordinateUpdateTask = null;
                }
            }

            base.AfterParentChanged();
        }
    }
}