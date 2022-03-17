﻿#region "copyright"

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
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.ViewModels;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System.Threading;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Solar System Body Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but an object with orbital elements can be specified inside. These don't track at the sidereal rate, so their coordinates are constantly updated.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SolarSystemBodyContainer : SequenceContainer, IDeepSkyObjectContainer {
        private readonly IProfileService profileService;
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly Task coordinateUpdateTask;
        private readonly CancellationTokenSource coordinateUpdateCts;
        private INighttimeCalculator nighttimeCalculator;
        private InputTarget target;

        [ImportingConstructor]
        public SolarSystemBodyContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator) : this(profileService, nighttimeCalculator, applicationMediator, OrbitalsPlugin.OrbitalElementsAccessor) {
        }

        public SolarSystemBodyContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator,
            IOrbitalElementsAccessor orbitalElementsAccessor) : base(new SequentialStrategy()) {
            this.profileService = profileService;
            this.nighttimeCalculator = nighttimeCalculator;
            this.applicationMediator = applicationMediator;
            _ = Task.Run(() => NighttimeData = nighttimeCalculator.Calculate());
            this.orbitalElementsAccessor = orbitalElementsAccessor;

            var defaultSolarSystemBody = SolarSystemBody.Moon;
            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new SolarSystemBodyObject(orbitalElementsAccessor, defaultSolarSystemBody, profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            coordinateUpdateCts = new CancellationTokenSource();
            coordinateUpdateTask = Task.Run(() => CoordinateUpdateLoop(coordinateUpdateCts.Token));

            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.LocationChanged), ProfileService_LocationChanged);
            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.HorizonChanged), ProfileService_HorizonChanged);
        }

        [JsonProperty]
        public SolarSystemBody SelectedSolarSystemBody {
            get => TargetObject.SolarSystemBody;
            set {
                if (TargetObject.SolarSystemBody != value) {
                    TargetObject.SolarSystemBody = value;
                    Target.TargetName = value.ToString();
                    Name = value.ToString();
                    RefreshCoordinates();
                    RaisePropertyChanged();
                }
            }
        }

        private async Task CoordinateUpdateLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested) {
                RefreshCoordinates();

                // TODO: Make this configurable
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }

        private void RefreshCoordinates() {
            Target.InputCoordinates.Coordinates = Target.DeepSkyObject.Coordinates;
            ShiftTrackingRate = Target.DeepSkyObject.ShiftTrackingRate;
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

        public override void Teardown() {
            base.Teardown();

            coordinateUpdateCts?.Cancel();
        }

        private SolarSystemBodyObject TargetObject => (SolarSystemBodyObject)Target.DeepSkyObject;

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
            var clone = new SolarSystemBodyContainer(profileService, nighttimeCalculator, applicationMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition))
            };

            clone.TargetObject.SolarSystemBody = TargetObject.SolarSystemBody;
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