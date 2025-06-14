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
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {
    [ExportMetadata("Name", "Slew to TLE Location")]
    [ExportMetadata("Description", "Slews ahead of a TLE object's expected location and then waits for it before tracking")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Telescope")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class TleSlew : SequenceItem, IValidatable {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IOrbitalsOptions options;

        [ImportingConstructor]
        public TleSlew(ITelescopeMediator telescopeMediator, IGuiderMediator guiderMediator) {
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.options = OrbitalsPlugin.OrbitalsOptions;
        }

        private TleSlew(TleSlew cloneMe) : this(cloneMe.telescopeMediator, cloneMe.guiderMediator) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new TleSlew(this);
        }

        public ManualTLEContainer ParentContainer { get; private set; }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var telescopeInfo = telescopeMediator.GetInfo();
            var guiderInfo = guiderMediator.GetInfo();
            var useGuider = guiderInfo.Connected && guiderInfo.CanSetShiftRate;
            var useTelescopeRates = TleUtil.TelescopeSupportsShiftRate(telescopeInfo);
            var useTelescopeTracking = telescopeInfo.CanSetTrackingEnabled;
            Logger.Info($"Beginning TLE Slew for {ParentContainer.Name}. Using Guider: {useGuider}, Telescope Tracking: {useTelescopeTracking}");
            ParentContainer.TrackingEnabled = false;

            var targetObject = ParentContainer.TargetObject;
            int maxAttempts = 2;
            int attemptCount = 0;
            bool objectReached = false;
            while (attemptCount++ < maxAttempts && !objectReached) {
                var nowPosition = targetObject.PositionAt(DateTime.UtcNow);
                DateTime slewAt = DateTime.UtcNow + TimeSpan.FromSeconds(this.options.TLETrackStartWaitTime_sec);
                var futurePosition = targetObject.PositionAt(slewAt);

                if (useGuider) {
                    await guiderMediator.StopGuiding(token);
                }

                if (useTelescopeTracking) {
                    telescopeMediator.SetTrackingEnabled(false);
                }
                if (useTelescopeRates) {
                    telescopeMediator.SetCustomTrackingRate(SiderealShiftTrackingRate.Disabled);
                }

                Logger.Info($"Starting slew for SlewAndTrack, to {futurePosition.Coordinates}({futurePosition.TopoCoordinates}). Updated from {nowPosition.Coordinates}({nowPosition.TopoCoordinates}) at {slewAt}");
                if (!await telescopeMediator.SlewToTopocentricCoordinates(futurePosition.TopoCoordinates, token)) {
                    throw new SequenceEntityFailedException("TLE slew failed");
                }
                Logger.Info($"Completed slew for SlewAndTrack. At {telescopeMediator.GetCurrentPosition()}");
                if (useTelescopeRates) {
                    telescopeMediator.SetCustomTrackingRate(SiderealShiftTrackingRate.Disabled);
                }
                telescopeMediator.SetTrackingMode(TrackingMode.Stopped);
                Logger.Info($"Slewed to where object will be at {slewAt}. Waiting until then before resuming");

                var waitRemaining = slewAt - DateTime.UtcNow;
                if (waitRemaining < TimeSpan.Zero) {
                    if (attemptCount < maxAttempts) {
                        var warningMessage = $"TLE object slew didn't complete before the wait period. {maxAttempts - attemptCount} attempts remaining";
                        Logger.Warning(warningMessage);
                        Notification.ShowWarning(warningMessage);
                        continue;
                    } else {
                        var warningMessage = "TLE object slew didn't complete before the wait period. Consider increasing the wait time in the plugin options.";
                        Logger.Warning(warningMessage);
                        Notification.ShowWarning(warningMessage);
                    }
                } else {
                    await CoreUtil.Wait(waitRemaining, token, progress, "Waiting for object to reach start");
                    progress.Report(new ApplicationStatus { Status = string.Empty });
                    objectReached = true;
                }

                if (useTelescopeTracking) {
                    Logger.Info("Target time reached. Enabling tracking");
                    telescopeMediator.SetTrackingEnabled(true);
                }
                Logger.Info($"Setting custom tracking rate to RA: {futurePosition.TrackingRate.RASecondsPerSiderealSecond}, Dec: {futurePosition.TrackingRate.DecArcsecsPerSec}");
                telescopeMediator.SetTrackingMode(TrackingMode.Custom);
                if (useTelescopeRates && !telescopeMediator.SetCustomTrackingRate(futurePosition.TrackingRate)) {
                    throw new Exception("Failed to set custom tracking rate");
                }

                if (useGuider) {
                    await guiderMediator.StartGuiding(false, progress, token);
                    await guiderMediator.SetShiftRate(futurePosition.TrackingRate, token);
                }
            }
            ParentContainer.TrackingEnabled = true;
        }

        public bool Validate() {
            var i = new List<string>();
            var info = telescopeMediator.GetInfo();
            if (ParentContainer == null) {
                i.Add("Must be within a TLE container");
            } else if (!info.Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }

            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            ParentContainer = SequenceUtility.GetParent<ManualTLEContainer>(this);
            Validate();
        }

        public override string ToString() {
            return $"TleSlew: {Category}, Item: {nameof(TleSlew)}";
        }
    }
}