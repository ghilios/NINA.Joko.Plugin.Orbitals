using ASCOM.Astrometry;
using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {
    abstract public class OrbitalsContainerBase<T> : SequenceContainer, IDeepSkyObjectContainer where T : OrbitalsObjectBase {
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
            coordinateUpdateCts = new CancellationTokenSource();
            coordinateUpdateTask = Task.Run(() => CoordinateUpdateLoop(coordinateUpdateCts.Token));

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
            while (!ct.IsCancellationRequested) {
                RefreshCoordinates();

                await Task.Delay(TimeSpan.FromSeconds(this.orbitalsOptions.OrbitalPositionRefreshTime_sec), ct);
            }
        }

        protected T TargetObject => (T)Target.DeepSkyObject;

        protected void RefreshCoordinates() {
            try {
                var targetCoordinates = Target.DeepSkyObject.Coordinates.Clone();
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
                Target.InputCoordinates.Coordinates = targetCoordinates;
                ShiftTrackingRate = Target.DeepSkyObject.ShiftTrackingRate;
                DistanceAU = TargetObject.Position.Distance;
                AfterParentChanged();
            } catch (Exception e) {
                Logger.Error("Error while refreshing coordinates", e);
            }
        }

        public override void Teardown() {
            base.Teardown();

            coordinateUpdateCts?.Cancel();
        }

        private SiderealShiftTrackingRate shiftTrackingRate = SiderealShiftTrackingRate.Disabled;

        public SiderealShiftTrackingRate ShiftTrackingRate {
            get => shiftTrackingRate;
            private set {
                shiftTrackingRate = value;
                RaisePropertyChanged();
            }
        }

        private double distanceAU = 0.0;

        public double DistanceAU {
            get => distanceAU;
            private set {
                distanceAU = value;
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
    }
}
