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
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Utility;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Set Telescope Tracking Rate")]
    [ExportMetadata("Description", "Sets the rate for tracking a target that does not use the sidereal tracking rate.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Telescope")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetTelescopeShiftRate : SequenceItem, IValidatable {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IOrbitalsOptions options;

        [ImportingConstructor]
        public SetTelescopeShiftRate(ITelescopeMediator telescopeMediator) {
            this.telescopeMediator = telescopeMediator;
            this.options = OrbitalsPlugin.OrbitalsOptions;
        }

        private SetTelescopeShiftRate(SetTelescopeShiftRate cloneMe) : this(cloneMe.telescopeMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new SetTelescopeShiftRate(this);
        }

        public SiderealShiftTrackingRate ShiftTrackingRate { get; private set; } = SiderealShiftTrackingRate.Disabled;

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (ShiftTrackingRate.Enabled) {
                var adjustedRate = ShiftTrackingRate.AdjustForASCOM(this.options);
                if (!telescopeMediator.SetCustomTrackingRate(adjustedRate.RAArcsecsPerSec, adjustedRate.DecArcsecsPerSec)) {
                    throw new SequenceEntityFailedException($"Setting tracking rate to {adjustedRate} failed");
                }
            } else {
                if (!telescopeMediator.SetTrackingMode(Equipment.Interfaces.TrackingMode.Sidereal)) {
                    throw new SequenceEntityFailedException($"Setting tracking rate to sidereal failed");
                }
            }
            return Task.CompletedTask;
        }

        public bool Validate() {
            var i = new List<string>();
            var info = telescopeMediator.GetInfo();
            if (!info.Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            } else if (!info.CanSetRightAscensionRate) {
                i.Add($"{info.Name} guider does not support setting the RA rate. Try using guider shift tracking.");
            } else if (!info.CanSetDeclinationRate) {
                i.Add($"{info.Name} guider does not support setting the Dec rate. Try using guider shift tracking.");
            } else if (!ShiftTrackingRate.Enabled) {
                i.Add($"No target object set which requires custom tracking");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            var contextCoordinates = ItemUtility.RetrieveContextCoordinates(this.Parent);
            if (contextCoordinates != null) {
                ShiftTrackingRate = contextCoordinates.ShiftTrackingRate;
            } else {
                ShiftTrackingRate = SiderealShiftTrackingRate.Disabled;
            }
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(SetTelescopeShiftRate)}";
        }
    }
}