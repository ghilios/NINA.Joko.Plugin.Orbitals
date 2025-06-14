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
using NINA.Joko.Plugin.Orbitals.Utility;
using SGPdotNET.TLE;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    [ExportMetadata("Name", "Manual TLE Body Sequence")]
    [ExportMetadata("Description", "Works like a sequential instruction set, but an object with a TLE can be manually specified inside. These don't track at the sidereal rate, so their coordinates are constantly updated.")]
    [ExportMetadata("Icon", "OrbitSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Container")]
    [Export(typeof(ISequenceItem))]
    [Export(typeof(ISequenceContainer))]
    [JsonObject(MemberSerialization.OptIn)]
    public class ManualTLEContainer : OrbitalsContainerBase<TLEObject> {
        private readonly IApplicationMediator applicationMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;

        [ImportingConstructor]
        public ManualTLEContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            ITelescopeMediator telescopeMediator,
            IApplicationMediator applicationMediator,
            IGuiderMediator guiderMediator) : this(profileService, nighttimeCalculator, applicationMediator, telescopeMediator, guiderMediator, OrbitalsPlugin.OrbitalsOptions) {
        }

        public ManualTLEContainer(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IApplicationMediator applicationMediator,
            ITelescopeMediator telescopeMediator,
            IGuiderMediator guiderMediator,
            IOrbitalsOptions orbitalsOptions) : base(profileService, nighttimeCalculator, orbitalsOptions) {
            this.applicationMediator = applicationMediator;
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;

            var epoch = telescopeMediator.GetInfo().EquatorialSystem;

            Target = new InputTarget(Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude), profileService.ActiveProfile.AstrometrySettings.Horizon);
            Target.DeepSkyObject = new TLEObject(null, profileService.ActiveProfile.AstrometrySettings.Horizon, profileService, epoch, TimeSpan.FromSeconds(orbitalsOptions.TLEPositionRefreshTime_sec));
            Target.DeepSkyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);

            PostConstruction();
        }

        private String tleData = "";

        [JsonProperty]
        public String TLEData {
            get => tleData;
            set {
                if (tleData != value) {
                    if (!TleUtil.ParseTle(value, out Tle parsedTle)) {
                        Notification.ShowWarning($"Invalid TLE data");
                        return;
                    }

                    this.tleData = value;
                    TargetObject.Tle = parsedTle;
                    Target.TargetName = parsedTle.Name;
                    Name = parsedTle.Name;
                    RefreshCoordinates();
                    RaiseAllPropertiesChanged();
                }
            }
        }

        private double targetAltitude = double.NaN;

        public double TargetAltitude {
            get => targetAltitude;
            private set {
                if (value != targetAltitude) {
                    targetAltitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double targetAzimuth = double.NaN;

        public double TargetAzimuth {
            get => targetAzimuth;
            private set {
                if (value != targetAzimuth) {
                    targetAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        protected override int GetCoordinateRefreshTime() {
            return this.orbitalsOptions.TLEPositionRefreshTime_sec;
        }

        public override object Clone() {
            var clone = new ManualTLEContainer(profileService, nighttimeCalculator, telescopeMediator, applicationMediator, guiderMediator) {
                Icon = Icon,
                Name = Name,
                Category = Category,
                Description = Description,
                Items = new ObservableCollection<ISequenceItem>(Items.Select(i => i.Clone() as ISequenceItem)),
                Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(t => t.Clone() as ISequenceTrigger)),
                Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(t => t.Clone() as ISequenceCondition))
            };

            clone.TargetObject.Tle = TargetObject.Tle;
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

        private bool trackingEnabled = false;

        public bool TrackingEnabled {
            get => trackingEnabled;
            set {
                if (trackingEnabled != value) {
                    trackingEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        protected override async Task AfterRefreshCoordinates(CancellationToken ct) {
            await base.AfterRefreshCoordinates(ct);
            if (TargetObject == null) {
                return;
            }
            TargetAltitude = Position.TopoCoordinates?.Altitude?.Degree ?? double.NaN;
            TargetAzimuth = Position.TopoCoordinates?.Azimuth?.Degree ?? double.NaN;

            if (!TrackingEnabled) {
                return;
            }

            try {
                var telescopeInfo = telescopeMediator.GetInfo();
                if (!telescopeInfo.Connected) {
                    Logger.Error("Disabling TLE tracking. Telescope not connected");
                    Notification.ShowError("Disabling TLE tracking. Telescope not connected");
                    TrackingEnabled = false;
                    return;
                }

                var guiderInfo = guiderMediator.GetInfo();
                var telescopeTrack = CanTelescopeTrack();
                var guiderTrack = guiderInfo.Connected && guiderInfo.CanSetShiftRate;
                if (!telescopeTrack && !guiderTrack) {
                    Logger.Error("Disabling TLE tracking. Telescope can no longer track");
                    Notification.ShowError("Disabling TLE tracking. Neither telescope nor guider can track");
                    TrackingEnabled = false;
                    return;
                }

                var trackingRate = TargetObject.ShiftTrackingRate;
                if (telescopeTrack) {
                    SetTelescopeShiftRate(trackingRate);
                }
                if (guiderTrack) {
                    await SetGuiderShiftRate(trackingRate, ct);
                }
            } catch (Exception e) {
                Logger.Error(e, "Failed refreshing TLE tracking rates");
                Notification.ShowError($"Error when refreshing TLE tracking rates. {e.Message}");
                TrackingEnabled = false;
            }
        }

        private bool CanTelescopeTrack() {
            var info = telescopeMediator.GetInfo();
            return info.Connected && TargetObject.Tle != null && TleUtil.TelescopeSupportsShiftRate(info);
        }

        private async Task<bool> SetGuiderShiftRate(SiderealShiftTrackingRate trackingRate, CancellationToken ct) {
            try {
                if (!await this.guiderMediator.SetShiftRate(trackingRate, ct)) {
                    Logger.Error("Failed to set guider shift rate");
                    return false;
                }
                return true;
            } catch (Exception e) {
                Logger.Error("Failed to set guider shift rate", e);
                return false;
            }
        }

        private bool SetTelescopeShiftRate(SiderealShiftTrackingRate trackingRate) {
            try {
                if (!this.telescopeMediator.SetCustomTrackingRate(trackingRate.ApplyQuirks(this.orbitalsOptions.QuirksMode))) {
                    Logger.Error("Failed to set telescope shift rate");
                    return false;
                }
                return true;
            } catch (Exception e) {
                Logger.Error("Failed to set telescope shift rate", e);
                return false;
            }
        }

        public override void SequenceBlockStarted() {
            base.SequenceBlockStarted();
            TrackingEnabled = false;
        }

        public override void SequenceBlockFinished() {
            base.SequenceBlockFinished();
            TrackingEnabled = false;
        }
    }
}