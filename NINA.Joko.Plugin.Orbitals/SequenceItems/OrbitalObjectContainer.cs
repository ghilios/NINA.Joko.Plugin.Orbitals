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
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.ViewModels;
using System.Threading;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Orbital Object Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but a comet, asteroid, or other object with orbital elements can be specified inside. These don't track at the sidereal rate, so their coordinates are constantly updated.")]
    [ExportMetadata("Icon", "CometSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class OrbitalObjectContainer : SequenceContainer, IDeepSkyObjectContainer {
        private readonly IProfileService profileService;
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly Task coordinateUpdateTask;
        private readonly CancellationTokenSource coordinateUpdateCts;
        private InputTarget target;

        [ImportingConstructor]
        public OrbitalObjectContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator) : this(profileService, nighttimeCalculator, applicationMediator, OrbitalsPlugin.OrbitalElementsAccessor, OrbitalsPlugin.OrbitalsOptions) {
        }

        public OrbitalObjectContainer(
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
            this.OrbitalSearchVM = new OrbitalSearchVM(orbitalElementsAccessor);
            OrbitalSearchVM.PropertyChanged += OrbitalSearchVM_PropertyChanged;

            this.orbitalElementsAccessor = orbitalElementsAccessor;
            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new OrbitalElementsObject(orbitalElementsAccessor, null, profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            coordinateUpdateCts = new CancellationTokenSource();
            coordinateUpdateTask = Task.Run(() => CoordinateUpdateLoop(coordinateUpdateCts.Token));

            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.LocationChanged), ProfileService_LocationChanged);
            WeakEventManager<IProfileService, EventArgs>.AddHandler(profileService, nameof(profileService.HorizonChanged), ProfileService_HorizonChanged);
        }

        private void OrbitalSearchVM_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(OrbitalSearchVM.SelectedOrbitalElements)) {
                TargetObject.OrbitalElements = OrbitalSearchVM.SelectedOrbitalElements;
                var targetName = TargetObject.OrbitalElements.Name;
                Target.TargetName = targetName;
                Name = targetName;
                RaisePropertyChanged(nameof(SelectedOrbitalName));
                RefreshCoordinates();
            }
        }

        public OrbitalObjectTypeEnum ObjectType {
            get => OrbitalSearchVM.ObjectType;
            set {
                if (OrbitalSearchVM.ObjectType != value) {
                    OrbitalSearchVM.ObjectType = value;
                    RaisePropertyChanged();
                }
            }
        }

        [JsonProperty]
        public string SelectedOrbitalName {
            get => TargetObject.OrbitalElements?.Name;
            set {
                var currentName = TargetObject.OrbitalElements?.Name;
                if (currentName != value) {
                    string targetName = OrbitalElementsObject.NotSetName;
                    if (string.IsNullOrEmpty(value)) {
                        TargetObject.OrbitalElements = null;
                        OrbitalSearchVM.SetTargetNameWithoutSearch("");
                    } else {
                        try {
                            var orbitalElements = orbitalElementsAccessor.Get(ObjectType, value);
                            if (orbitalElements != null) {
                                TargetObject.OrbitalElements = orbitalElements;
                                targetName = TargetObject.OrbitalElements.Name;
                                OrbitalSearchVM.SetTargetNameWithoutSearch(targetName);
                            } else {
                                TargetObject.OrbitalElements = null;
                                OrbitalSearchVM.SetTargetNameWithoutSearch(value);
                                Logger.Warning($"Orbital object {value}({ObjectType}) not found");
                                Notification.ShowWarning($"Orbital object {value}({ObjectType}) not found");
                            }
                        } catch (Exception e) {
                            Logger.Error($"Failed to get orbital object {value}({ObjectType})", e);
                            Notification.ShowError($"Failed to get orbital object. {e.Message}");
                        }
                    }

                    Target.TargetName = targetName;
                    Name = targetName;

                    RefreshCoordinates();
                    RaisePropertyChanged();
                }
            }
        }

        public IOrbitalSearchVM OrbitalSearchVM { get; private set; }

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

        private OrbitalElementsObject TargetObject => (OrbitalElementsObject)Target.DeepSkyObject;

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
            var clone = new OrbitalObjectContainer(profileService, nighttimeCalculator, applicationMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition))
            };

            clone.TargetObject.OrbitalElements = TargetObject.OrbitalElements;
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