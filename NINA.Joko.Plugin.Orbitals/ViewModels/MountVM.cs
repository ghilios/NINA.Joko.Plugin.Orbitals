#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Equipment;
using NINA.Joko.Plugin.TenMicron.Interfaces;
using NINA.Joko.Plugin.TenMicron.Utility;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Input;
using NINA.Joko.Plugin.TenMicron.Model;
using System.Windows;
using NINA.Equipment.Interfaces;

namespace NINA.Joko.Plugin.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountVM : DockableVM, IMountVM, ITelescopeConsumer {
        private readonly IMount mount;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IMountMediator mountMediator;
        private DeviceUpdateTimer updateTimer;
        private bool disposed = false;
        private bool supportedMountConnected = false;
        private bool previousTelescopeConnected = false;

        [ImportingConstructor]
        public MountVM(IProfileService profileService, ITelescopeMediator telescopeMediator) :
            this(profileService, telescopeMediator, TenMicronPlugin.Mount, TenMicronPlugin.MountMediator) {
        }

        public MountVM(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IMount mount,
            IMountMediator mountMediator) : base(profileService) {
            this.Title = "10u Mount Info";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugin.TenMicron;component/Resources/SVGDataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["TenMicronSVG"];
            ImageGeometry.Freeze();

            this.mount = mount;
            this.telescopeMediator = telescopeMediator;
            this.mountMediator = mountMediator;

            this.telescopeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterHandler(this);

            ResetMeridianSlewLimitCommand = new RelayCommand(ResetMeridianSlewLimit);
            ResetSlewSettleLimitCommand = new RelayCommand(ResetSlewSettleTime);
        }

        private void BroadcastMountInfo() {
            this.mountMediator.Broadcast(MountInfo);
        }

        private void ResetMeridianSlewLimit(object o) {
            try {
                this.mount.SetMeridianSlewLimit(0);
            } catch (Exception e) {
                Notification.ShowError($"Failed to reset meridian limit: {e.Message}");
                Logger.Error(e);
            }
        }

        private void ResetSlewSettleTime(object o) {
            try {
                this.mount.SetSlewSettleTime(decimal.Zero);
            } catch (Exception e) {
                Notification.ShowError($"Failed to reset slew settle time: {e.Message}");
                Logger.Error(e);
            }
        }

        public void Dispose() {
            if (!this.disposed) {
                updateTimer?.Stop();
                this.telescopeMediator.RemoveConsumer(this);
                this.disposed = true;
            }
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            if (previousTelescopeConnected == deviceInfo.Connected) {
                return;
            }

            try {
                if (deviceInfo.Connected) {
                    try {
                        ProductFirmware productFirmware = null;
                        try {
                            productFirmware = mount.GetProductFirmware();
                        } catch (Exception e) {
                            Logger.Error("Failed to query product firmware after telescope connected", e);
                            Notification.ShowWarning($"Not a 10u mount. 10u utilities disabled.");
                            return;
                        }

                        if (!MountUtility.IsSupportedProduct(productFirmware)) {
                            Logger.Error($"{productFirmware.ProductName} is not a supported 10u mount. 10u utilities disabled");
                            Notification.ShowInformation($"{productFirmware.ProductName} is not a supported 10u mount. 10u utilities disabled");
                            return;
                        }

                        var ascomConfig = MountUtility.GetMountAscomConfig(deviceInfo.DeviceId);
                        if (ascomConfig != null) {
                            if (!MountUtility.ValidateMountAscomConfig(ascomConfig)) {
                                Logger.Error($"ASCOM configuration validation failed. Leaving 10u mount utilities disconnected");
                                return;
                            }

                            RefractionOverrideFilePath = ascomConfig.RefractionUpdateFile;
                        }

                        mount.SetMaximumPrecision(productFirmware);
                        MountInfo.ProductFirmware = productFirmware;
                        MountInfo.MountId = mount.GetId();
                        MountInfo.Status = MountStatusEnum.Unknown;
                        supportedMountConnected = true;
                        MountInfo.Connected = true;

                        // This cannot be initialized in the constructor because it is running in an Async context at that time, which causes UpdateMountValues to never fire
                        _ = updateTimer?.Stop();
                        updateTimer = new DeviceUpdateTimer(
                            GetMountValues,
                            UpdateMountValues,
                            profileService.ActiveProfile.ApplicationSettings.DevicePollingInterval
                        );
                        updateTimer.Start();
                    } catch (Exception e) {
                        Notification.ShowError($"Failed to connect 10u utilities. {e.Message}");
                    }
                } else {
                    _ = updateTimer?.Stop();

                    MountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();
                    supportedMountConnected = false;
                }
            } finally {
                previousTelescopeConnected = deviceInfo.Connected;
            }
        }

        private MountInfo mountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();

        public MountInfo MountInfo {
            get => mountInfo;
            private set {
                mountInfo = value;
                RaisePropertyChanged();
            }
        }

        private string refractionOverrideFilePath;

        public string RefractionOverrideFilePath {
            get => refractionOverrideFilePath;
            set {
                if (refractionOverrideFilePath != value) {
                    this.refractionOverrideFilePath = value;
                    RaisePropertyChanged();
                }
            }
        }

        public async Task Disconnect() {
            if (updateTimer != null) {
                await updateTimer.Stop();
            }

            MountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();
            MountInfo.Status = MountStatusEnum.Unknown;
            supportedMountConnected = false;

            BroadcastMountInfo();
        }

        private void UpdateMountValues(Dictionary<string, object> mountValues) {
            object o = null;
            mountValues.TryGetValue(nameof(MountInfo.Connected), out o);
            MountInfo.Connected = (bool)(o ?? false);

            if (!MountInfo.Connected) {
                _ = Disconnect();
                return;
            }

            mountValues.TryGetValue(nameof(MountInfo.UnattendedFlipEnabled), out o);
            MountInfo.UnattendedFlipEnabled = (bool)(o ?? false);
            UnattendedFlipEnabled = MountInfo.UnattendedFlipEnabled;

            mountValues.TryGetValue(nameof(MountInfo.TrackingRateArcsecPerSec), out o);
            MountInfo.TrackingRateArcsecPerSec = (decimal)(o ?? decimal.Zero);

            mountValues.TryGetValue(nameof(MountInfo.Status), out o);
            MountInfo.Status = (MountStatusEnum)(o ?? MountStatusEnum.Unknown);

            mountValues.TryGetValue(nameof(MountInfo.SlewSettleTimeSeconds), out o);
            MountInfo.SlewSettleTimeSeconds = (decimal)(o ?? decimal.Zero);

            mountValues.TryGetValue(nameof(MountInfo.MeridianLimitDegrees), out o);
            MountInfo.MeridianLimitDegrees = (int)(o ?? 0);

            BroadcastMountInfo();
        }

        private Dictionary<string, object> GetMountValues() {
            var mountValues = new Dictionary<string, object>();
            try {
                if (!supportedMountConnected) {
                    return mountValues;
                }

                mountValues.Add(nameof(MountInfo.Connected), true);
                mountValues.Add(nameof(MountInfo.UnattendedFlipEnabled), this.mount.GetUnattendedFlipEnabled().Value);
                mountValues.Add(nameof(MountInfo.TrackingRateArcsecPerSec), this.mount.GetTrackingRateArcsecsPerSec().Value);
                mountValues.Add(nameof(MountInfo.Status), this.mount.GetStatus().Value);
                mountValues.Add(nameof(MountInfo.SlewSettleTimeSeconds), this.mount.GetSlewSettleTimeSeconds().Value);
                mountValues.Add(nameof(MountInfo.MeridianLimitDegrees), this.mount.GetMeridianSlewLimitDegrees().Value);
                return mountValues;
            } catch (Exception e) {
                if (telescopeMediator.GetInfo().Connected) {
                    Notification.ShowError($"Failed to retrieve mount properties. 10u mount utilities disconnected");
                    Logger.Error("Failed while retrieving 10u mount properties", e);
                }

                mountValues.Clear();
                mountValues.Add(nameof(MountInfo.Connected), false);
                return mountValues;
            }
        }

        public ICommand ResetMeridianSlewLimitCommand { get; private set; }
        public ICommand ResetSlewSettleLimitCommand { get; private set; }

        private bool unattendedFlipEnabled;

        public bool UnattendedFlipEnabled {
            get => unattendedFlipEnabled;
            set {
                if (unattendedFlipEnabled != value) {
                    unattendedFlipEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public void DisableUnattendedFlip() {
            try {
                mount.SetUnattendedFlip(false);
                UnattendedFlipEnabled = false;
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError($"Failed to disable unattended flip: {ex.Message}");
            }
        }

        public Task<IList<string>> Rescan() {
            throw new NotImplementedException();
        }

        public Task<bool> Connect() {
            throw new NotImplementedException();
        }

        public MountInfo GetDeviceInfo() {
            return MountInfo;
        }

        public CoordinateAngle GetMountReportedDeclination() {
            if (supportedMountConnected) {
                return mount.GetDeclination();
            }
            return null;
        }

        public AstrometricTime GetMountReportedRightAscension() {
            if (supportedMountConnected) {
                return mount.GetRightAscension();
            }
            return null;
        }

        public AstrometricTime GetMountReportedLocalSiderealTime() {
            if (supportedMountConnected) {
                return mount.GetLocalSiderealTime();
            }
            return null;
        }

        public bool SetTrackingRate(TrackingMode trackingMode) {
            if (supportedMountConnected) {
                switch (trackingMode) {
                    case TrackingMode.Sidereal:
                        mount.SetSiderealTrackingRate();
                        mount.StartTracking();
                        return true;

                    case TrackingMode.Solar:
                        mount.SetSolarTrackingRate();
                        mount.StartTracking();
                        return true;

                    case TrackingMode.Lunar:
                        mount.SetLunarTrackingRate();
                        mount.StartTracking();
                        return true;

                    case TrackingMode.Stopped:
                        mount.StopTracking();
                        return true;

                    case TrackingMode.King:
                        Logger.Error("King rate is not supported");
                        return false;

                    case TrackingMode.Custom:
                        Logger.Error("Custom rate is not supported");
                        return false;
                }

                throw new ArgumentException($"Unknown tracking mode received: {trackingMode}");
            }
            return false;
        }
    }
}