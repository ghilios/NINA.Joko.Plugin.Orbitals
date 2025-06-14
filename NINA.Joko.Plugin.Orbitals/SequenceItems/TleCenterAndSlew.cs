#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.SequenceItem.Platesolving;
using NINA.Sequencer.SequenceItem.Telescope;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Center and slew to TLE Location")]
    [ExportMetadata("Description", "Slews ahead of a TLE object's expected location and then waits for it before tracking. Additionally syncs and solved first.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Telescope")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TleCenterAndSlew : SequentialContainer, IImmutableContainer {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IOrbitalsOptions options;

        private readonly IProfileService profileService;
        private readonly IImagingMediator imagingMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IDomeMediator domeMediator;
        private readonly IDomeFollower domeFollower;
        private readonly IPlateSolverFactory plateSolverFactory;
        private readonly IWindowServiceFactory windowServiceFactory;

        [OnDeserializing]
        public void OnDeserializing(StreamingContext context) {
            this.Items.Clear();
            this.Conditions.Clear();
            this.Triggers.Clear();
        }

        [ImportingConstructor]
        public TleCenterAndSlew(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IImagingMediator imagingMediator,
            IFilterWheelMediator filterWheelMediator,
            IGuiderMediator guiderMediator,
            IDomeMediator domeMediator,
            IDomeFollower domeFollower,
            IPlateSolverFactory plateSolverFactory,
            IWindowServiceFactory windowServiceFactory) {
            this.profileService = profileService; ;
            this.telescopeMediator = telescopeMediator;
            this.imagingMediator = imagingMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.domeMediator = domeMediator;
            this.domeFollower = domeFollower;
            this.guiderMediator = guiderMediator;
            this.plateSolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            this.options = OrbitalsPlugin.OrbitalsOptions;

            var setTracking = new SetTracking(telescopeMediator);
            setTracking.TrackingMode = TrackingMode.Sidereal;
            this.Add(setTracking);
            this.Add(new Center(profileService, telescopeMediator, imagingMediator, filterWheelMediator, guiderMediator, domeMediator, domeFollower, plateSolverFactory, windowServiceFactory));
            this.Add(new TleSlew(telescopeMediator, guiderMediator));
            IsExpanded = false;
        }

        private TleCenterAndSlew(TleCenterAndSlew cloneMe) : this(
            cloneMe.profileService,
            cloneMe.telescopeMediator,
            cloneMe.imagingMediator,
            cloneMe.filterWheelMediator,
            cloneMe.guiderMediator,
            cloneMe.domeMediator,
            cloneMe.domeFollower,
            cloneMe.plateSolverFactory,
            cloneMe.windowServiceFactory) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new TleCenterAndSlew(this);
        }

        public override bool Validate() {
            var issues = new List<string>();
            var center = GetCenter();
            var tleSlew = GetTleSlew();

            bool valid = false;

            valid = center.Validate() && valid;
            issues.AddRange(center.Issues);

            valid = tleSlew.Validate() && valid;
            issues.AddRange(tleSlew.Issues);

            Issues = issues;
            RaisePropertyChanged(nameof(Issues));

            return valid;
        }

        public Center GetCenter() {
            return Items[1] as Center;
        }

        public TleSlew GetTleSlew() {
            return Items[2] as TleSlew;
        }

        public override string ToString() {
            return $"TleCenterAndSlew: {Category}, Item: {nameof(TleCenterAndSlew)}";
        }

        public override Task Interrupt() {
            return this.Parent?.Interrupt();
        }
    }
}