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

    /// <summary>
    /// Non-generic surface for the click-handler that opens the RA/Dec offset
    /// modal. WPF binds the button to the concrete container as <c>DataContext</c>;
    /// this interface lets the handler poke at it without knowing T.
    /// </summary>
    public interface IOrbitalsOffsetContainer {
        double OffsetSeparationArcsec { get; set; }
        double OffsetPositionAngleDeg { get; set; }
        double DerivedRAOffsetHours { get; }
        double DerivedDecOffsetDegrees { get; }

        /// <summary>
        /// Convert an RA/Dec offset (anchored at the current body position) into
        /// the canonical Separation + Offset PA and apply it to this container.
        /// </summary>
        void SetOffsetFromRADec(double raOffsetHours, double decOffsetDegrees);
    }

    public abstract class OrbitalsContainerBase<T> : SequenceContainer, IDeepSkyObjectContainer, IOrbitalsOffsetContainer where T : OrbitalsObjectBase {
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

        /// <summary>
        /// Retained as a [JsonProperty] for backward compatibility with sequences
        /// saved before Separation+PA became the canonical offset. The
        /// <see cref="OnOrbitalsDeserialized"/> hook detects legacy state (nonzero
        /// RA/Dec, zero Separation+PA) and migrates it on the first refresh.
        /// Going forward this field is unused at runtime — derived display values
        /// come from <see cref="OffsetRADisplay"/> / <see cref="OffsetDecDisplay"/>.
        /// </summary>
        [JsonProperty]
        public InputCoordinatesEx OffsetCoordinates {
            get => offsetCoordinates;
            set {
                offsetCoordinates = value;
                RaisePropertyChanged();
            }
        }

        private double offsetSeparationArcsec;

        /// <summary>
        /// Canonical angular separation between the body's true position and the
        /// framed target, in arcseconds. Position-independent — slewing math
        /// uses <see cref="OrbitalOffsetMath.ApplyOffset"/> with this value plus
        /// <see cref="OffsetPositionAngleDeg"/>.
        /// </summary>
        [JsonProperty]
        public double OffsetSeparationArcsec {
            get => offsetSeparationArcsec;
            set {
                if (offsetSeparationArcsec != value) {
                    offsetSeparationArcsec = value;
                    RaisePropertyChanged();
                    RaiseOffsetChanged();
                }
            }
        }

        private double offsetPositionAngleDeg;

        /// <summary>
        /// Canonical position angle of the framed target as seen from the body's
        /// true position, measured North-through-East in degrees, [0, 360).
        /// </summary>
        [JsonProperty]
        public double OffsetPositionAngleDeg {
            get => offsetPositionAngleDeg;
            set {
                if (offsetPositionAngleDeg != value) {
                    offsetPositionAngleDeg = value;
                    RaisePropertyChanged();
                    RaiseOffsetChanged();
                }
            }
        }

        // Migration latch: set in OnDeserialized when a pre-Sep/PA save is detected.
        // Cleared on the first successful RefreshCoordinates, which has access to
        // the target body's position and can convert the legacy RA/Dec offset.
        private bool legacyOffsetMigrationPending = false;

        private bool deserializing = false;

        [OnDeserializing]
        public void OnOrbitalsDeserializing(StreamingContext context) {
            deserializing = true;
        }

        [OnDeserialized]
        public void OnOrbitalsDeserialized(StreamingContext context) {
            deserializing = false;
            if (offsetSeparationArcsec == 0.0 && offsetPositionAngleDeg == 0.0
                && offsetCoordinates != null
                && (offsetCoordinates.Coordinates.RA != 0.0 || offsetCoordinates.Coordinates.Dec != 0.0)) {
                legacyOffsetMigrationPending = true;
            }
            RaiseOffsetChanged();
        }

        private void RaiseOffsetChanged() {
            if (!deserializing) {
                RaisePropertyChanged(nameof(OffsetSeparationArcsec));
                RaisePropertyChanged(nameof(OffsetPositionAngleDeg));
                RaisePropertyChanged(nameof(OffsetRADisplay));
                RaisePropertyChanged(nameof(OffsetDecDisplay));
                RaisePropertyChanged(nameof(OffsetSeparationDisplay));
                RefreshCoordinates();
            }
        }

        /// <summary>Pretty-print the angular separation as d°m′s″.</summary>
        public string OffsetSeparationDisplay {
            get {
                var totalArcsec = Math.Abs(offsetSeparationArcsec);
                var deg = (int)(totalArcsec / 3600.0);
                var arcmin = (int)((totalArcsec - deg * 3600.0) / 60.0);
                var arcsec = totalArcsec - deg * 3600.0 - arcmin * 60.0;
                return $"{deg:D2}° {arcmin:D2}′ {arcsec:F1}″";
            }
        }

        /// <summary>Read-only RA offset (the shifted target's RA minus the body's RA).</summary>
        public string OffsetRADisplay => FormatRAOffset(_derivedRAOffsetHours);

        /// <summary>Read-only Dec offset (the shifted target's Dec minus the body's Dec).</summary>
        public string OffsetDecDisplay => FormatDecOffset(_derivedDecOffsetDegrees);

        // Derived RA/Dec offset, refreshed from Sep+PA at the current body position
        // every time RefreshCoordinates runs. Used by the new offset display panel.
        private double _derivedRAOffsetHours;
        private double _derivedDecOffsetDegrees;

        public double DerivedRAOffsetHours => _derivedRAOffsetHours;
        public double DerivedDecOffsetDegrees => _derivedDecOffsetDegrees;

        public void SetOffsetFromRADec(double raOffsetHours, double decOffsetDegrees) {
            try {
                var origin = TargetObject.PositionAt(DateTime.UtcNow).Coordinates;
                var shiftedDec = Math.Max(-90.0, Math.Min(90.0, origin.Dec + decOffsetDegrees));
                var shifted = new Coordinates(
                    Angle.ByHours(AstroUtil.EuclidianModulus(origin.RA + raOffsetHours, 24.0)),
                    Angle.ByDegree(shiftedDec),
                    origin.Epoch);
                OffsetSeparationArcsec = OrbitalOffsetMath.AngularSeparation(origin, shifted);
                OffsetPositionAngleDeg = OrbitalOffsetMath.PositionAngleNToE(origin, shifted);
            } catch (Exception ex) {
                Logger.Error("Could not convert RA/Dec offset to Separation+PA", ex);
            }
        }

        private static string FormatRAOffset(double hours) {
            var sign = hours < 0 ? "-" : "+";
            var abs = Math.Abs(hours);
            var h = (int)abs;
            var m = (int)((abs - h) * 60.0);
            var s = (abs - h - m / 60.0) * 3600.0;
            return $"{sign}{h:D2}h {m:D2}m {s:F1}s";
        }

        private static string FormatDecOffset(double degrees) {
            var sign = degrees < 0 ? "-" : "+";
            var abs = Math.Abs(degrees);
            var d = (int)abs;
            var m = (int)((abs - d) * 60.0);
            var s = (abs - d - m / 60.0) * 3600.0;
            return $"{sign}{d:D2}° {m:D2}′ {s:F1}″";
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
                var originalTarget = targetPosition.Coordinates;
                var targetCoordinates = originalTarget;

                // Legacy migration: convert the saved RA/Dec offset to Separation+PA
                // now that we have a target position to anchor it to.
                if (legacyOffsetMigrationPending && offsetCoordinates != null) {
                    var legacyRA = AstroUtil.EuclidianModulus(originalTarget.RA + offsetCoordinates.Coordinates.RA, 24.0);
                    var legacyDec = originalTarget.Dec + offsetCoordinates.Coordinates.Dec;
                    if (legacyDec >= -90.0 && legacyDec <= 90.0) {
                        var legacyShifted = new Coordinates(
                            Angle.ByHours(legacyRA),
                            Angle.ByDegree(legacyDec),
                            originalTarget.Epoch);
                        offsetSeparationArcsec = OrbitalOffsetMath.AngularSeparation(originalTarget, legacyShifted);
                        offsetPositionAngleDeg = OrbitalOffsetMath.PositionAngleNToE(originalTarget, legacyShifted);
                        RaisePropertyChanged(nameof(OffsetSeparationArcsec));
                        RaisePropertyChanged(nameof(OffsetPositionAngleDeg));
                        RaisePropertyChanged(nameof(OffsetSeparationDisplay));
                    }
                    legacyOffsetMigrationPending = false;
                }

                if (offsetSeparationArcsec > 0.0) {
                    var shifted = OrbitalOffsetMath.ApplyOffset(originalTarget, offsetSeparationArcsec, offsetPositionAngleDeg);
                    if (shifted.Dec < -90.0 || shifted.Dec > 90.0) {
                        Notification.ShowWarning("Invalid dec after applying offset. Resetting offset.");
                        offsetSeparationArcsec = 0.0;
                        offsetPositionAngleDeg = 0.0;
                        RaisePropertyChanged(nameof(OffsetSeparationArcsec));
                        RaisePropertyChanged(nameof(OffsetPositionAngleDeg));
                        RaisePropertyChanged(nameof(OffsetSeparationDisplay));
                    } else {
                        targetCoordinates = shifted;
                    }
                }

                // Derive the RA/Dec offset (display only). Wrap dRA into [-12, 12)
                // so small offsets render as a small signed value rather than
                // jumping near the 0/24 boundary.
                var dRa = targetCoordinates.RA - originalTarget.RA;
                if (dRa > 12.0) dRa -= 24.0;
                if (dRa < -12.0) dRa += 24.0;
                _derivedRAOffsetHours = dRa;
                _derivedDecOffsetDegrees = targetCoordinates.Dec - originalTarget.Dec;
                RaisePropertyChanged(nameof(OffsetRADisplay));
                RaisePropertyChanged(nameof(OffsetDecDisplay));

                Position = targetPosition;
                Target.InputCoordinates.Coordinates = targetCoordinates;
                ShiftTrackingRate = Target.DeepSkyObject.ShiftTrackingRate;
                Distance.AU = TargetObject.Position.Distance;
                CurrentCoordinates_RAString = targetCoordinates.RAString;
                CurrentCoordinates_DecString = targetCoordinates.DecString;
                RaisePropertyChanged(nameof(CurrentCoordinates));
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

        public Coordinates CurrentCoordinates => Target?.InputCoordinates?.Coordinates;


        private string raString;
        public string CurrentCoordinates_RAString {
            get => raString;
            set {
                raString = value;
                RaisePropertyChanged();
            }
        }

        private string decString;
        public string CurrentCoordinates_DecString {
            get => decString;
            set {
                decString = value;
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