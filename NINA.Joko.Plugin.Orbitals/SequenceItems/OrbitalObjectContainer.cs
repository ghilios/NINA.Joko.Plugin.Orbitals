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
using System.ComponentModel;
using NINA.Joko.Plugin.Orbitals.Utility;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Orbital Object Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but a comet, asteroid, or other object with orbital elements can be specified inside. These don't track at the sidereal rate, so their coordinates are constantly updated.")]
    [ExportMetadata("Icon", "CometSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class OrbitalObjectContainer : OrbitalsContainerBase<OrbitalElementsObject> {
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;

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
            IOrbitalsOptions orbitalsOptions) : base(profileService, nighttimeCalculator, orbitalsOptions) {
            this.applicationMediator = applicationMediator;
            var orbitalSearchVM = new OrbitalSearchVM(orbitalElementsAccessor);
            this.OrbitalSearchVM = orbitalSearchVM;

            this.orbitalElementsAccessor = orbitalElementsAccessor;
            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new OrbitalElementsObject(orbitalElementsAccessor, null, profileService.ActiveProfile.AstrometrySettings.Horizon, profileService);
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            WeakEventManager<INotifyPropertyChanged, PropertyChangedEventArgs>.AddHandler(orbitalSearchVM, nameof(orbitalSearchVM.PropertyChanged), OrbitalSearchVM_PropertyChanged);
            WeakEventManager<IOrbitalElementsAccessor, OrbitalElementsObjectTypeUpdatedEventArgs>.AddHandler(orbitalElementsAccessor, nameof(orbitalElementsAccessor.Updated), OrbitalElementsAccessor_Updated);
            PostConstruction();
        }

        private void OrbitalElementsAccessor_Updated(object sender, OrbitalElementsObjectTypeUpdatedEventArgs e) {
            try {
                if (e.ObjectType == OrbitalSearchVM.ObjectType) {
                    TargetObject.Update();
                    RefreshCoordinates();
                }
            } catch (Exception ex) {
                Notification.ShowError($"Failed to reload {e.ObjectType.ToDescriptionString()} data in advanced sequencer after update. {ex.Message}");
                Logger.Error($"Failed to reload {e.ObjectType} data in advanced sequencer after update", ex);
            }
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

        [JsonProperty]
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
                    TargetObject.Update();
                    Name = targetName;

                    RefreshCoordinates();
                    RaisePropertyChanged();
                }
            }
        }

        public IOrbitalSearchVM OrbitalSearchVM { get; private set; }

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
            clone.Target.PositionAngle = this.Target.PositionAngle;

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
            return $"{baseString}, Target: {Target?.TargetName} {Target?.DeepSkyObject?.Coordinates} {Target?.PositionAngle}";
        }
    }
}