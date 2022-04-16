#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Set Telescope Tracking Rate")]
    [ExportMetadata("Description", "Automatically updates the rate for tracking a target that does not use the sidereal tracking rate. The shift rate changes over time, so this trigger keeps it updated between exposures.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Telescope")]
    [Export(typeof(ISequenceTrigger))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetTelescopeShiftRateTrigger : SequenceTrigger, IValidatable {
        private readonly ITelescopeMediator telescopeMediator;

        [ImportingConstructor]
        public SetTelescopeShiftRateTrigger(ITelescopeMediator telescopeMediator) : base() {
            this.telescopeMediator = telescopeMediator;
            TriggerRunner.Add(new SetTelescopeShiftRate(telescopeMediator));
        }

        private SetTelescopeShiftRateTrigger(SetTelescopeShiftRateTrigger cloneMe) : this(cloneMe.telescopeMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new SetTelescopeShiftRateTrigger(this) {
                TriggerRunner = (SequentialContainer)TriggerRunner.Clone()
            };
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = ImmutableList.CreateRange(value);
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(ISequenceContainer context, IProgress<ApplicationStatus> progress, CancellationToken token) {
            await TriggerRunner.Run(progress, token);
        }

        public override bool ShouldTrigger(ISequenceItem previousItem, ISequenceItem nextItem) {
            if (nextItem is IExposureItem) {
                var takeExposure = (IExposureItem)nextItem;
                return takeExposure.ImageType == "LIGHT";
            }
            return false;
        }

        public override string ToString() {
            return $"Trigger: {nameof(SetTelescopeShiftRateTrigger)}";
        }

        public bool Validate() {
            TriggerRunner.Validate();
            var i = new List<string>(TriggerRunner.Issues);
            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            TriggerRunner.AttachNewParent(this.Parent);
            base.AfterParentChanged();
        }
    }
}