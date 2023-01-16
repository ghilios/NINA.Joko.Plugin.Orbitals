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
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Collections.ObjectModel;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Conditions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Enums;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Solar System Body Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but an object with orbital elements can be specified inside. These don't track at the sidereal rate, so their coordinates are constantly updated.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SolarSystemBodyContainer : OrbitalsContainerBase<SolarSystemBodyObject> {
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;

        [ImportingConstructor]
        public SolarSystemBodyContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator) : this(profileService, nighttimeCalculator, applicationMediator, OrbitalsPlugin.OrbitalElementsAccessor, OrbitalsPlugin.OrbitalsOptions) {
        }

        public SolarSystemBodyContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator,
            IOrbitalElementsAccessor orbitalElementsAccessor,
            IOrbitalsOptions orbitalsOptions) : base(profileService, nighttimeCalculator, orbitalsOptions) {
            this.applicationMediator = applicationMediator;
            this.orbitalElementsAccessor = orbitalElementsAccessor;

            var defaultSolarSystemBody = SolarSystemBody.Moon;
            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new SolarSystemBodyObject(orbitalElementsAccessor, defaultSolarSystemBody, profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            PostConstruction();
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
    }
}