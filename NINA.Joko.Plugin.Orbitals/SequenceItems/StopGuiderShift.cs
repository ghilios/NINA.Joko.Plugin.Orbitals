#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Stop Guider Track Shift")]
    [ExportMetadata("Description", "Disables guider track shift, if it is enabled. Otherwise does nothing.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Guider")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class StopGuiderShift : SequenceItem, IValidatable {
        private IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public StopGuiderShift(IGuiderMediator guiderMediator) {
            this.guiderMediator = guiderMediator;
        }

        private StopGuiderShift(StopGuiderShift cloneMe) : this(cloneMe.guiderMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new StopGuiderShift(this);
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (!await guiderMediator.StopShifting(token)) {
                throw new SequenceEntityFailedException("Stopping shift rate failed");
            }
        }

        public bool Validate() {
            var i = new List<string>();
            var info = guiderMediator.GetInfo();
            if (!info.Connected) {
                i.Add(Loc.Instance["LblGuiderNotConnected"]);
            } else if (!info.CanSetShiftRate) {
                i.Add($"{info.Name} guider does not support shift rates. Try PHD2.");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(StopGuiderShift)}";
        }
    }
}