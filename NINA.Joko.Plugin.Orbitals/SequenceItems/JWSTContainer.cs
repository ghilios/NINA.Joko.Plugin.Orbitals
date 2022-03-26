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
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.Container.ExecutionStrategy;
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.ObjectModel;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Conditions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System.Threading;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "James-Webb Space Telescope Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but tracks the JWST. It does't track at the sidereal rate, so its coordinates are constantly updated.")]
    [ExportMetadata("Icon", "JWSTSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class JWSTContainer : SequenceContainer, IDeepSkyObjectContainer {
        private readonly IProfileService profileService;
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly Task coordinateUpdateTask;
        private readonly CancellationTokenSource coordinateUpdateCts;
        private INighttimeCalculator nighttimeCalculator;
        private InputTarget target;

        [ImportingConstructor]
        public JWSTContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator) : this(profileService, nighttimeCalculator, applicationMediator, OrbitalsPlugin.OrbitalElementsAccessor, OrbitalsPlugin.OrbitalsOptions) {
        }

        public JWSTContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator,
            IOrbitalElementsAccessor orbitalElementsAccessor,
            IOrbitalsOptions orbitalsOptions) : base(new SequentialStrategy()) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationMediator = applicationMediator;
            this.orbitalsOptions = orbitalsOptions;
            _ = Task.Run(() => NighttimeData = nighttimeCalculator.Calculate());
            this.orbitalElementsAccessor = orbitalElementsAccessor;

            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new PVTableObject(orbitalElementsAccessor, "James-Webb Space Telescope", profileService.ActiveProfile.AstrometrySettings.Horizon, profileService);
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            coordinateUpdateCts = new CancellationTokenSource();
            coordinateUpdateTask = Task.Run(() => CoordinateUpdateLoop(coordinateUpdateCts.Token));

            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.LocationChanged), ProfileService_LocationChanged);
            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.HorizonChanged), ProfileService_HorizonChanged);
            WeakEventManager<IOrbitalElementsAccessor, VectorTableUpdatedEventArgs>.AddHandler(this.orbitalElementsAccessor, nameof(this.orbitalElementsAccessor.VectorTableUpdated), OrbitalElementsAccessor_VectorTableUpdated);
        }

        private void OrbitalElementsAccessor_VectorTableUpdated(object sender, VectorTableUpdatedEventArgs e) {
            try {
                TargetObject.Update();
                RefreshCoordinates();
            } catch (Exception ex) {
                Notification.ShowError($"Failed to reload JWST data in advanced sequencer after update. {ex.Message}");
                Logger.Error("Failed to reload JWST data in advanced sequencer after update", ex);
            }
        }

        private async Task CoordinateUpdateLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                RefreshCoordinates();

                await Task.Delay(TimeSpan.FromSeconds(this.orbitalsOptions.OrbitalPositionRefreshTime_sec), ct);
            }
        }

        private void RefreshCoordinates() {
            Target.InputCoordinates.Coordinates = Target.DeepSkyObject.Coordinates;
            ShiftTrackingRate = Target.DeepSkyObject.ShiftTrackingRate;
            DistanceAU = TargetObject.Position.Distance;
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

        private double distanceAU = 0.0;

        public double DistanceAU {
            get => distanceAU;
            private set {
                distanceAU = value;
                RaisePropertyChanged();
            }
        }

        public override void Teardown() {
            base.Teardown();

            coordinateUpdateCts?.Cancel();
        }

        private PVTableObject TargetObject => (PVTableObject)Target.DeepSkyObject;

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

        public override object Clone() {
            var clone = new JWSTContainer(profileService, nighttimeCalculator, applicationMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition))
            };

            clone.Target.Rotation = this.Target.Rotation;

            foreach (var item in clone.Items) {
                item.AttachNewParent(clone);
            }

            foreach (var condition in clone.Conditions) {
                condition.AttachNewParent(clone);
            }

            foreach (var trigger in clone.Triggers) {
                trigger.AttachNewParent(clone);
            }

            return clone;
        }

        public override string ToString() {
            var baseString = base.ToString();
            return $"{baseString}, Target: {Target?.TargetName} {Target?.DeepSkyObject?.Coordinates} {Target?.Rotation}";
        }
    }
}