#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Imaging;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Joko.Plugin.Orbitals.View;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;
using SGPdotNET.TLE;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    [Export(typeof(IDockableVM))]
    public class OrbitalsVM : DockableVM {
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IGuiderMediator guiderMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IApplicationMediator applicationMediator;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly IJPLAccessor jplAccessor;
        private readonly IMPCAccessor mpcAccessor;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IProfileService profileService;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IProgress<ApplicationStatus> progress;
        private readonly IEnumerable<ICaptureSource> captureSources;
        private bool initialLoadComplete;
        private Task<bool> refreshTask;

        [ImportingConstructor]
        public OrbitalsVM(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IGuiderMediator guiderMediator,
            ITelescopeMediator telescopeMediator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IApplicationStatusMediator applicationStatusMediator,
            [ImportMany] IEnumerable<ICaptureSource> captureSources)
            : this(profileService, nighttimeCalculator, guiderMediator, telescopeMediator, framingAssistantVM, applicationMediator, applicationStatusMediator, captureSources, OrbitalsPlugin.OrbitalsOptions, OrbitalsPlugin.JPLAccessor, OrbitalsPlugin.MPCAccessor, OrbitalsPlugin.OrbitalElementsAccessor, new OrbitalSearchVM(OrbitalsPlugin.OrbitalElementsAccessor)) {
        }

        public OrbitalsVM(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IGuiderMediator guiderMediator,
            ITelescopeMediator telescopeMediator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IEnumerable<ICaptureSource> captureSources,
            IOrbitalsOptions orbitalsOptions,
            IJPLAccessor jplAccessor,
            IMPCAccessor mpcAccessor,
            IOrbitalElementsAccessor orbitalElementsAccessor,
            IOrbitalSearchVM orbitalSearchVM) : base(profileService) {
            this.Title = "Orbitals";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugin.Orbitals;component/Resources/SVGDataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["OrbitSVG"];
            ImageGeometry.Freeze();

            this.nighttimeCalculator = nighttimeCalculator;
            this.guiderMediator = guiderMediator;
            this.telescopeMediator = telescopeMediator;
            this.framingAssistantVM = framingAssistantVM;
            this.applicationMediator = applicationMediator;
            this.orbitalsOptions = orbitalsOptions;
            this.jplAccessor = jplAccessor;
            this.mpcAccessor = mpcAccessor;
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.OrbitalSearchVM = orbitalSearchVM;
            this.profileService = profileService;
            this.applicationStatusMediator = applicationStatusMediator;
            this.captureSources = captureSources;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Orbitals");
            this.orbitalElementsAccessor.Updated += OrbitalElementsAccessor_Updated;
            this.orbitalElementsAccessor.VectorTableUpdated += OrbitalElementsAccessor_VectorTableUpdated;
            var initialLoadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            _ = Task.Run(async () => {
                try {
                    await orbitalElementsAccessor.Load(this.progress, initialLoadCts.Token);
                    InitialLoadComplete = true;
                } catch (Exception e) {
                    Logger.Error("Initial orbital elements load failed", e);
                }
            }, initialLoadCts.Token);

            this.UpdateCometElementsCommand = new AsyncRelayCommand(UpdateCometElements, () => InitialLoadComplete);
            this.UpdateCometElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.ClearCometElementsCommand = new AsyncRelayCommand(ClearCometElements, () => InitialLoadComplete);
            this.ClearCometElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.UpdateNumberedAsteroidElementsCommand = new AsyncRelayCommand(UpdateNumberedAsteroids, () => InitialLoadComplete);
            this.UpdateNumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.ClearNumberedAsteroidElementsCommand = new AsyncRelayCommand(ClearNumberedAsteroids, () => InitialLoadComplete);
            this.ClearNumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.UpdateUnnumberedAsteroidElementsCommand = new AsyncRelayCommand(UpdateUnnumberedAsteroids, () => InitialLoadComplete);
            this.UpdateUnnumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.ClearUnnumberedAsteroidElementsCommand = new AsyncRelayCommand(ClearUnnumberedAsteroids, () => InitialLoadComplete);
            this.ClearUnnumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.UpdateJWSTVectorTableCommand = new AsyncRelayCommand(UpdateJWSTVectorTable, () => InitialLoadComplete);
            this.UpdateJWSTVectorTableCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.CancelUpdateCometElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateCometElementsTask, updateCometElementsCts));
            this.CancelUpdateNumberedAsteroidElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateNumberedAsteroidsTask, updateNumberedAsteroidsCts));
            this.CancelUpdateUnnumberedAsteroidElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateUnnumberedAsteroidsTask, updateUnnumberedAsteroidsCts));
            this.CancelUpdateJWSTVectorTableCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateJWSTVectorTableTask, updateJWSTVectorTableCts));

            this.LoadSelectionCommand = new RelayCommand(LoadSelectionClicked, CanLoad);
            this.LoadSelectionCommand.RegisterPropertyChangeNotification(this, nameof(SearchObjectType), nameof(ManualTLEInput));
            this.LoadSelectionCommand.RegisterPropertyChangeNotification(OrbitalSearchVM, nameof(OrbitalSearchVM.SelectedOrbitalElements));

            this.SendToFramingWizardCommand = new AsyncRelayCommand(SendToFramingWizardCommandAction, () => SelectedOrbitalsObject != null);
            this.SendToFramingWizardCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));

            this.SlewAndTrackCommand = new AsyncRelayCommand(SlewAndTrackCommandAction, CanSlewAndTrack);
            this.SlewAndTrackCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));
            this.SlewAndTrackCommand.RegisterPropertyChangeNotification(telescopeMediator.GetInfo(), nameof(TelescopeInfo.Connected));

            this.CancelSlewAndTrackCommand = new AsyncRelayCommand(CancelSlewAndTrack);

            this.SetTrackingRateCommand = new RelayCommand(SetTrackingRateCommandAction, CanSetTrackingRate);
            this.SetTrackingRateCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));
            this.SetTrackingRateCommand.RegisterPropertyChangeNotification(telescopeMediator.GetInfo(), nameof(TelescopeInfo.Connected), nameof(TelescopeInfo.CanSetRightAscensionRate), nameof(TelescopeInfo.CanSetDeclinationRate));

            this.SetGuiderShiftCommand = new AsyncRelayCommand(SetGuiderShiftCommandAction, CanSetGuiderShift);
            this.SetGuiderShiftCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));
            this.SetGuiderShiftCommand.RegisterPropertyChangeNotification(guiderMediator.GetInfo(), nameof(GuiderInfo.Connected), nameof(GuiderInfo.CanSetShiftRate));

            this.ResetOffsetCommand = new RelayCommand(ResetOffset, () => SelectedOrbitalsObject != null && (RAOffset != 0.0d || DecOffset != 0.0d));
            this.ResetOffsetCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject), nameof(RAOffset), nameof(DecOffset));

            this.SetOffsetCommand = new RelayCommand(SetOffset, () => SelectedOrbitalsObject != null && telescopeMediator.GetInfo().Connected);
            this.SetOffsetCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));
            this.SetOffsetCommand.RegisterPropertyChangeNotification(telescopeMediator.GetInfo(), nameof(TelescopeInfo.Connected));

            orbitalsOptions.PropertyChanged += OrbitalsOptions_PropertyChanged;
        }

        private void OrbitalsOptions_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
        }

        private Task<bool> SendToFramingWizardCommandAction() {
            return Task.Run(async () => {
                if (SelectedOrbitalsObject == null) {
                    Notification.ShowWarning("No orbital object selected");
                    return false;
                }

                try {
                    var wizardVm = new OrbitalFramingWizardVM(
                        profileService,
                        captureSources,
                        nighttimeCalculator,
                        applicationStatusMediator,
                        orbitalsOptions);

                    await Application.Current.Dispatcher.InvokeAsync(() => {
                        wizardVm.Initialize(SelectedOrbitalsObject);
                        var window = new OrbitalFramingWizardView {
                            DataContext = wizardVm,
                            Owner = Application.Current.MainWindow
                        };
                        window.ShowDialog();
                    });

                    return true;
                } catch (Exception e) {
                    Notification.ShowError($"Failed to open orbital framing wizard. {e.Message}");
                    Logger.Error("Failed to open orbital framing wizard", e);
                    return false;
                }
            });
        }

        private CancellationTokenSource slewAndTrackCts;

        private Task<bool> SlewAndTrackCommandAction() {
            var newSlewAndTrackCts = new CancellationTokenSource();
            CancellationToken ct = newSlewAndTrackCts.Token;

            var existingRefreshTask = refreshTask;

            return Task.Run(async () => {
                Logger.Info("Starting SlewAndTrackCommandAction");

                if (SelectedOrbitalsObject == null) {
                    Notification.ShowWarning("No orbital object selected");
                    return false;
                }
                var telescopeInfo = telescopeMediator.GetInfo();
                if (!telescopeInfo.Connected) {
                    Notification.ShowWarning("Telescope not connected");
                    return false;
                }

                ToggleRefreshEnabled(false);
                if (existingRefreshTask != null) {
                    Logger.Info("Waiting for existing refresh task to terminate");
                    await existingRefreshTask.WaitAsync(ct);
                    Logger.Info("Existing refresh task terminated");
                }

                slewAndTrackCts = newSlewAndTrackCts;

                bool setTelescopeTracking = telescopeInfo.CanSetRightAscensionRate && telescopeInfo.CanSetDeclinationRate;
                bool setGuiderTracking = CanSetGuiderShift();

                try {
                    bool slewAheadAndWait = SearchObjectType == SearchObjectTypeEnum.ManualTLE;
                    DateTime slewAt = DateTime.UtcNow;
                    if (slewAheadAndWait) {
                        slewAt += TimeSpan.FromSeconds(orbitalsOptions.TLETrackStartWaitTime_sec);
                    }

                    var slewPosition = SelectedOrbitalsObject.PositionAt(slewAt);

                    var adjustedCoordinates = slewPosition.Coordinates.Clone();
                    adjustedCoordinates.RA += RAOffset;
                    adjustedCoordinates.Dec += DecOffset;

                    if (slewAheadAndWait) {
                        // Slew to topocentric coordinates so that the scope is not tracking at its destination
                        var latitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Latitude);
                        var longitude = Angle.ByDegree(profileService.ActiveProfile.AstrometrySettings.Longitude);
                        var elevation = profileService.ActiveProfile.AstrometrySettings.Elevation;
                        var topocentricCoordinates = adjustedCoordinates.Transform(latitude, longitude, elevation, 0.0d, 0.0d, 0.0d, 0.0d, slewAt);

                        telescopeMediator.SetTrackingEnabled(false);
                        if (setTelescopeTracking) {
                            telescopeMediator.SetCustomTrackingRate(SiderealShiftTrackingRate.Disabled);
                        }
                        Logger.Info($"Starting slew for SlewAndTrack, to {topocentricCoordinates}. Updated from {adjustedCoordinates} at {slewAt}");
                        await telescopeMediator.SlewToTopocentricCoordinates(topocentricCoordinates, ct);
                        Logger.Info($"Completed slew for SlewAndTrack. At {telescopeMediator.GetCurrentPosition()}");

                        // Disable tracking again for good measure
                        if (setTelescopeTracking) {
                            telescopeMediator.SetCustomTrackingRate(SiderealShiftTrackingRate.Disabled);
                        }
                        telescopeMediator.SetTrackingEnabled(false);
                        telescopeMediator.SetTrackingMode(TrackingMode.Stopped);
                        Logger.Info($"Slewed to where object will be at {slewAt}. Waiting until then before resuming");

                        var waitRemaining = slewAt - DateTime.UtcNow;
                        if (waitRemaining < TimeSpan.Zero) {
                            var warningMessage = "TLE object slew didn't complete before the wait period. Consider increasing the wait time in the plugin options.";
                            Logger.Warning(warningMessage);
                            Notification.ShowWarning(warningMessage);
                        } else {
                            await CoreUtil.Wait(waitRemaining, ct, this.progress, "Waiting for object to reach start");
                            progress.Report(new ApplicationStatus { Status = string.Empty });
                        }
                    } else {
                        await telescopeMediator.SlewToCoordinatesAsync(adjustedCoordinates, ct);
                    }

                    telescopeMediator.SetTrackingEnabled(true);
                    if (setTelescopeTracking) {
                        Logger.Info($"Setting custom tracking rate to RA: {slewPosition.TrackingRate.RASecondsPerSiderealSecond}, Dec: {slewPosition.TrackingRate.DecArcsecsPerSec}");
                        telescopeMediator.SetTrackingMode(TrackingMode.Custom);
                        if (!telescopeMediator.SetCustomTrackingRate(slewPosition.TrackingRate)) {
                            throw new Exception("Failed to set custom tracking rate");
                        }
                    }

                    telescopeInfo = telescopeMediator.GetInfo();
                    Task pulseTask = null;
                    if (telescopeInfo.CanPulseGuide) {
                        pulseTask = Task.Run(() => {
                            var currentCoordinates = telescopeInfo.Coordinates;
                            if (telescopeInfo.GuideRateDeclinationArcsecPerSec != 0) {
                                var decDifferenceDegrees = currentCoordinates.Dec - adjustedCoordinates.Dec;
                                var decDifferenceArcsec = decDifferenceDegrees * 3600.0d;

                                // South decreases Dec, North increases
                                GuideDirections decPulseDirection = decDifferenceArcsec > 0.0d ? GuideDirections.guideSouth : GuideDirections.guideNorth;

                                double seconds = Math.Abs(decDifferenceArcsec) / telescopeInfo.GuideRateDeclinationArcsecPerSec;
                                Logger.Info($"Pulsing {decPulseDirection} to adjust Dec for {seconds} seconds. Rate={telescopeInfo.GuideRateDeclinationArcsecPerSec} arcsec/sec");
                                telescopeMediator.PulseGuide(decPulseDirection, TimeSpan.FromSeconds(seconds).Milliseconds);
                            }
                            ct.ThrowIfCancellationRequested();
                            if (telescopeInfo.GuideRateRightAscensionArcsecPerSec != 0) {
                                var raDifferenceHours = currentCoordinates.RA - adjustedCoordinates.RA;
                                var raDifferenceArcsec = raDifferenceHours * 54000.0d;

                                // West decreases RA, East increases
                                GuideDirections raPulseDirection = raDifferenceArcsec > 0.0d ? GuideDirections.guideWest : GuideDirections.guideEast;

                                double seconds = Math.Abs(raDifferenceArcsec) / telescopeInfo.GuideRateRightAscensionArcsecPerSec;
                                Logger.Info($"Pulsing {raPulseDirection} to adjust RA for {seconds} seconds. Rate={telescopeInfo.GuideRateRightAscensionArcsecPerSec} arcsec/sec");
                                telescopeMediator.PulseGuide(raPulseDirection, TimeSpan.FromSeconds(seconds).Milliseconds);
                            }
                            ct.ThrowIfCancellationRequested();
                        }, ct);
                    }

                    if (setGuiderTracking) {
                        await guiderMediator.StartGuiding(false, this.progress, ct);
                        await guiderMediator.SetShiftRate(slewPosition.TrackingRate, ct);
                    }

                    await pulseTask;
                    int refreshTime = GetRefreshTime();
                    refreshTask = Task.Run(async () => {
                        try {
                            while (!ct.IsCancellationRequested) {
                                if (!CanSlewAndTrack()) {
                                    throw new Exception("Can no longer slew and track");
                                }

                                LoadSelection();
                                var slewPosition = SelectedOrbitalsObject.PositionAt(DateTime.UtcNow);
                                if (setTelescopeTracking && !telescopeMediator.SetCustomTrackingRate(slewPosition.TrackingRate)) {
                                    throw new Exception("Failed to set custom tracking rate");
                                }
                                if (setGuiderTracking) {
                                    await guiderMediator.SetShiftRate(slewPosition.TrackingRate, ct);
                                }
                                await Task.Delay(TimeSpan.FromSeconds(refreshTime), ct);
                            }
                            return true;
                        } catch (OperationCanceledException) {
                            Logger.Info("Tracking for orbital element cancelled");
                            RefreshEnabled = false;
                            refreshCts = null;
                            return false;
                        } catch (Exception e) {
                            Logger.Error("Failed to refresh tracking for orbital element", e);
                            Notification.ShowError($"Failed to track orbital element. {e.Message}");
                            RefreshEnabled = false;
                            refreshCts = null;
                            return false;
                        } finally {
                            if (setTelescopeTracking) {
                                telescopeMediator.SetCustomTrackingRate(SiderealShiftTrackingRate.Disabled);
                                telescopeMediator.SetTrackingMode(TrackingMode.Sidereal);
                            }
                            if (setGuiderTracking) {
                                // Fire and forget
                                _ = guiderMediator.SetShiftRate(SiderealShiftTrackingRate.Disabled, CancellationToken.None);
                            }
                        }
                    }, ct);
                    return await refreshTask;
                } catch (Exception e) {
                    Notification.ShowError($"Failed to initiate slew and track. {e.Message}");
                    Logger.Error("Failed to initiate slew and track", e);
                    return false;
                } finally {
                    progress.Report(new ApplicationStatus { Status = string.Empty });
                }
            });
        }

        private async Task<bool> CancelSlewAndTrack() {
            try {
                slewAndTrackCts?.Cancel();
                var localSlewAndTrackTask = refreshTask;
                if (refreshTask != null) {
                    await refreshTask;
                }
                return true;
            } catch (Exception) {
                return false;
            }
        }

        private bool InitialLoadComplete {
            get => initialLoadComplete;
            set {
                if (this.initialLoadComplete != value) {
                    this.initialLoadComplete = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool CanSlewAndTrack() {
            var info = telescopeMediator.GetInfo();
            return info.Connected && SelectedOrbitalsObject != null;
        }

        private bool CanSetTrackingRate() {
            var info = telescopeMediator.GetInfo();
            return info.Connected && info.CanSetRightAscensionRate && info.CanSetDeclinationRate && SelectedOrbitalsObject != null;
        }

        private void SetTrackingRateCommandAction() {
            try {
                if (!this.telescopeMediator.SetCustomTrackingRate(ShiftTrackingRate.ApplyQuirks(this.orbitalsOptions.QuirksMode))) {
                    Notification.ShowError("Failed to set orbital tracking rate");
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to set orbital tracking rate. {e.Message}");
                Logger.Error("Failed to set orbital tracking rate", e);
            }
        }

        private bool CanSetGuiderShift() {
            var info = guiderMediator.GetInfo();
            return info.Connected && info.CanSetShiftRate;
        }

        private Task<bool> SetGuiderShiftCommandAction() {
            return Task.Run(async () => {
                try {
                    if (!await this.guiderMediator.SetShiftRate(ShiftTrackingRate, CancellationToken.None)) {
                        Notification.ShowError("Failed to set guider shift rate");
                        return false;
                    }
                    return true;
                } catch (Exception e) {
                    Notification.ShowError($"Failed to set guider shift rate. {e.Message}");
                    Logger.Error("Failed to set guider shift rate", e);
                    return false;
                }
            });
        }

        private void OrbitalElementsAccessor_Updated(object sender, OrbitalElementsObjectTypeUpdatedEventArgs e) {
            if (e.ObjectType == OrbitalObjectTypeEnum.Comet) {
                CometCount = e.Count;
                CometLastUpdated = e.LastUpdated;
            } else if (e.ObjectType == OrbitalObjectTypeEnum.NumberedAsteroids) {
                NumberedAsteroidCount = e.Count;
                NumberedAsteroidLastUpdated = e.LastUpdated;
            } else if (e.ObjectType == OrbitalObjectTypeEnum.UnnumberedAsteroids) {
                UnnumberedAsteroidCount = e.Count;
                UnnumberedAsteroidLastUpdated = e.LastUpdated;
            }
        }

        private void OrbitalElementsAccessor_VectorTableUpdated(object sender, VectorTableUpdatedEventArgs e) {
            JWSTVectorTableValidUntil = e.ValidUntil;
        }

        private DateTime cometLastUpdated;

        public DateTime CometLastUpdated {
            get => cometLastUpdated;
            private set {
                cometLastUpdated = value;
                RaisePropertyChanged();
            }
        }

        private DateTime numberedAsteroidLastUpdated;

        public DateTime NumberedAsteroidLastUpdated {
            get => numberedAsteroidLastUpdated;
            private set {
                numberedAsteroidLastUpdated = value;
                RaisePropertyChanged();
            }
        }

        private DateTime unnumberedAsteroidLastUpdated;

        public DateTime UnnumberedAsteroidLastUpdated {
            get => unnumberedAsteroidLastUpdated;
            private set {
                unnumberedAsteroidLastUpdated = value;
                RaisePropertyChanged();
            }
        }

        private DateTime jwstVectorTableValidUntil;

        public DateTime JWSTVectorTableValidUntil {
            get => jwstVectorTableValidUntil;
            private set {
                jwstVectorTableValidUntil = value;
                RaisePropertyChanged();
            }
        }

        private NighttimeData nighttimeData;

        public NighttimeData NighttimeData {
            get => nighttimeData;
            private set {
                nighttimeData = value;
                RaisePropertyChanged();
            }
        }

        private int cometCount;

        public int CometCount {
            get => cometCount;
            private set {
                cometCount = value;
                RaisePropertyChanged();
            }
        }

        private int numberedAsteroidCount;

        public int NumberedAsteroidCount {
            get => numberedAsteroidCount;
            private set {
                numberedAsteroidCount = value;
                RaisePropertyChanged();
            }
        }

        private int unnumberedAsteroidCount;

        public int UnnumberedAsteroidCount {
            get => unnumberedAsteroidCount;
            private set {
                unnumberedAsteroidCount = value;
                RaisePropertyChanged();
            }
        }

        private SearchObjectTypeEnum searchObjectType = SearchObjectTypeEnum.SolarSystemBody;

        public SearchObjectTypeEnum SearchObjectType {
            get => searchObjectType;
            set {
                searchObjectType = value;
                if (searchObjectType != SearchObjectTypeEnum.SolarSystemBody) {
                    OrbitalSearchVM.ObjectType = SearchObjectType.ToOrbitalObjectTypeEnum();
                }

                RaisePropertyChanged();
            }
        }

        private SolarSystemBody selectedSolarSystemBody = SolarSystemBody.Moon;

        public SolarSystemBody SelectedSolarSystemBody {
            get => selectedSolarSystemBody;
            set {
                selectedSolarSystemBody = value;
                RaisePropertyChanged();
            }
        }

        private OrbitalsObjectBase selectedOrbitalsObject;

        public OrbitalsObjectBase SelectedOrbitalsObject {
            get => selectedOrbitalsObject;
            private set {
                selectedOrbitalsObject = value;
                SelectedOrbitalElementsObject = value as OrbitalElementsObject;
                RaisePropertyChanged();
            }
        }

        private OrbitalElementsObject selectedOrbitalElementsObject;

        public OrbitalElementsObject SelectedOrbitalElementsObject {
            get => selectedOrbitalElementsObject;
            private set {
                selectedOrbitalElementsObject = value;
                if (value != null) {
                    SelectedOrbitalPosition = Kepler.CalculateOrbitalElements(value.OrbitalElements, AstroUtil.GetJulianDate(DateTime.Now));
                } else {
                    SelectedOrbitalPosition = null;
                }
                RaisePropertyChanged();
            }
        }

        private OrbitalPosition selectedOrbitalPosition;

        public OrbitalPosition SelectedOrbitalPosition {
            get => selectedOrbitalPosition;
            set {
                selectedOrbitalPosition = value;
                RaisePropertyChanged();
            }
        }

        private Coordinates targetCoordinates;

        public Coordinates TargetCoordinates {
            get => targetCoordinates;
            private set {
                targetCoordinates = value;
                RaisePropertyChanged();
            }
        }

        private SiderealShiftTrackingRate shiftTrackingRate;

        public SiderealShiftTrackingRate ShiftTrackingRate {
            get => shiftTrackingRate;
            private set {
                shiftTrackingRate = value;
                RaisePropertyChanged();
            }
        }

        private Distance distance = new Distance(0.0d);

        public Distance Distance => distance;

        private double raOffset = 0.0d;

        public double RAOffset {
            get => raOffset;
            private set {
                raOffset = value;
                RaisePropertyChanged();
            }
        }

        private double decOffset = 0.0d;

        public double DecOffset {
            get => decOffset;
            private set {
                decOffset = value;
                RaisePropertyChanged();
            }
        }

        private double maxExposureSeconds = double.NaN;

        public double MaxExposureSeconds {
            get => maxExposureSeconds;
            private set {
                maxExposureSeconds = value;
                RaisePropertyChanged();
            }
        }

        private String manualTLEInput;

        public String ManualTLEInput {
            get => manualTLEInput;
            set {
                if (value != manualTLEInput) {
                    manualTLEInput = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool refreshEnabled;

        public bool RefreshEnabled {
            get => refreshEnabled;
            set {
                if (value != refreshEnabled) {
                    refreshEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        public IOrbitalsOptions Options => orbitalsOptions;

        public IOrbitalSearchVM OrbitalSearchVM { get; private set; }

        public AsyncRelayCommand CancelUpdateCometElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateCometElementsCommand { get; private set; }

        public AsyncRelayCommand ClearCometElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateNumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateNumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand ClearNumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateUnnumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateUnnumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand ClearUnnumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateJWSTVectorTableCommand { get; private set; }

        public AsyncRelayCommand UpdateJWSTVectorTableCommand { get; private set; }

        public RelayCommand LoadSelectionCommand { get; private set; }

        public AsyncRelayCommand SlewCommand { get; private set; }

        public AsyncRelayCommand SendToFramingWizardCommand { get; private set; }

        public AsyncRelayCommand SlewAndTrackCommand { get; private set; }

        public AsyncRelayCommand CancelSlewAndTrackCommand { get; private set; }

        public RelayCommand SetTrackingRateCommand { get; private set; }

        public AsyncRelayCommand SetGuiderShiftCommand { get; private set; }

        public RelayCommand ResetOffsetCommand { get; private set; }
        public RelayCommand SetOffsetCommand { get; private set; }

        // TODO: Refactor this the next time more orbital types are added
        private Task<bool> updateCometElementsTask;

        private CancellationTokenSource updateCometElementsCts;

        private Task<bool> updateNumberedAsteroidsTask;
        private CancellationTokenSource updateNumberedAsteroidsCts;

        private Task<bool> updateUnnumberedAsteroidsTask;
        private CancellationTokenSource updateUnnumberedAsteroidsCts;

        private Task<bool> updateJWSTVectorTableTask;
        private CancellationTokenSource updateJWSTVectorTableCts;

        private bool CanLoad() {
            if (SearchObjectType == SearchObjectTypeEnum.SolarSystemBody) {
                return true;
            } else if (SearchObjectType == SearchObjectTypeEnum.JWST) {
                return orbitalElementsAccessor.GetJWSTValidUntil() > DateTime.MinValue;
            } else if (SearchObjectType == SearchObjectTypeEnum.ManualTLE) {
                return TleUtil.ParseTle(ManualTLEInput, out var _);
            } else {
                return OrbitalSearchVM.SelectedOrbitalElements != null;
            }
        }

        private void LoadSelectionClicked() {
            ToggleRefreshEnabled(false);
            LoadSelection();
        }

        private void LoadSelection() {
            var objectType = SearchObjectType;
            try {
                NighttimeData = nighttimeCalculator.Calculate();
                if (objectType == SearchObjectTypeEnum.SolarSystemBody) {
                    LoadSolarSystemObject(SelectedSolarSystemBody);
                } else if (objectType == SearchObjectTypeEnum.JWST) {
                    LoadJWST();
                } else if (objectType == SearchObjectTypeEnum.ManualTLE) {
                    LoadManualTLE();
                } else {
                    LoadOrbitalObject(OrbitalSearchVM.SelectedOrbitalElements);
                }

                TargetCoordinates = SelectedOrbitalsObject.Coordinates;
                ShiftTrackingRate = SelectedOrbitalsObject.ShiftTrackingRate;
                Distance.AU = SelectedOrbitalsObject.Position.Distance;
                double arcsecPerSecondMovement = Math.Sqrt((ShiftTrackingRate.RAArcsecsPerSec * ShiftTrackingRate.RAArcsecsPerSec) + (ShiftTrackingRate.DecArcsecsPerSec * ShiftTrackingRate.DecArcsecsPerSec));
                double pixelScale = AstroUtil.ArcsecPerPixel(profileService.ActiveProfile.CameraSettings.PixelSize, profileService.ActiveProfile.TelescopeSettings.FocalLength);
                if (pixelScale > 0.0d && arcsecPerSecondMovement > 0.0d) {
                    MaxExposureSeconds = pixelScale / arcsecPerSecondMovement;
                } else {
                    MaxExposureSeconds = double.NaN;
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to load {objectType}. {e.Message}");
                Logger.Error($"Failed to load {objectType}", e);
            }
        }

        private void LoadJWST() {
            var jwstValidUntil = orbitalElementsAccessor.GetJWSTValidUntil();
            if (jwstValidUntil < DateTime.Now) {
                Notification.ShowError("JWST vector table expired");
                return;
            }

            var pvTableObject = new PVTableObject(orbitalElementsAccessor, "James-Webb Space Telescope", profileService.ActiveProfile.AstrometrySettings.Horizon, profileService);
            pvTableObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);
            SelectedOrbitalsObject = pvTableObject;
        }

        private void LoadManualTLE() {
            try {
                Tle tle = TleUtil.ParseTle(ManualTLEInput);
                var epoch = telescopeMediator.GetInfo().EquatorialSystem;

                var pvTableObject = new TLEObject(tle, profileService.ActiveProfile.AstrometrySettings.Horizon, profileService, epoch, TimeSpan.FromSeconds(orbitalsOptions.TLEPositionRefreshTime_sec));
                pvTableObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);
                SelectedOrbitalsObject = pvTableObject;
            } catch (Exception e) {
                Logger.Error(e, "Failed to load manual TLE");
                Notification.ShowError(e.Message);
            }
        }

        private void LoadOrbitalObject(Kepler.OrbitalElements orbitalElements) {
            if (orbitalElements == null) {
                Notification.ShowError("No orbital object selected");
                return;
            }
            var bodyObject = new OrbitalElementsObject(orbitalElementsAccessor, orbitalElements, profileService.ActiveProfile.AstrometrySettings.Horizon, profileService);
            bodyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);
            SelectedOrbitalsObject = bodyObject;
        }

        private void LoadSolarSystemObject(SolarSystemBody solarSystemBody) {
            var bodyObject = new SolarSystemBodyObject(orbitalElementsAccessor, solarSystemBody, profileService.ActiveProfile.AstrometrySettings.Horizon);
            bodyObject.SetDateAndPosition(NighttimeCalculator.GetReferenceDate(DateTime.Now), latitude: profileService.ActiveProfile.AstrometrySettings.Latitude, longitude: profileService.ActiveProfile.AstrometrySettings.Longitude);
            SelectedOrbitalsObject = bodyObject;
        }

        private void ResetOffset() {
            RAOffset = 0.0d;
            DecOffset = 0.0d;
        }

        private void SetOffset() {
            var targetCoordinates = TargetCoordinates;
            var currentCoordinates = this.telescopeMediator.GetCurrentPosition().Transform(targetCoordinates.Epoch);
            RAOffset = currentCoordinates.RA - targetCoordinates.RA;
            DecOffset = currentCoordinates.Dec - targetCoordinates.Dec;
        }

        public Task<bool> UpdateJWSTVectorTable() {
            if (updateJWSTVectorTableTask != null && !updateJWSTVectorTableTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateJWSTVectorTableCts = cts;

            var task = Task.Run(async () => {
                try {
                    var ct = cts.Token;
                    var vectorTable = await jplAccessor.GetJWSTVectorTable(DateTime.Now - TimeSpan.FromDays(1), TimeSpan.FromDays(8), ct);
                    await orbitalElementsAccessor.UpdateJWST(vectorTable.ToPVTable(), progress, cts.Token);
                    return true;
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception e) {
                    Logger.Error("Failed to update JWST vector table", e);
                    Notification.ShowError($"Failed to update JWST vector table. {e.Message}");
                    return false;
                }
            }, cts.Token);
            updateJWSTVectorTableTask = task;
            return task;
        }

        public Task<bool> UpdateCometElements() {
            if (updateCometElementsTask != null && !updateCometElementsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateCometElementsCts = cts;

            var task = Task.Run(async () => {
                try {
                    DateTime availableModifiedDate;
                    var ct = cts.Token;
                    if (orbitalsOptions.CometAccessor == OrbitalElementsAccessorEnum.JPL) {
                        availableModifiedDate = await jplAccessor.GetCometElementsLastModified(ct);
                    } else {
                        availableModifiedDate = await mpcAccessor.GetCometElementsLastModified(ct);
                    }

                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.Comet);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.Comet.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    if (orbitalsOptions.CometAccessor == OrbitalElementsAccessorEnum.JPL) {
                        var elements = await jplAccessor.GetCometElements(ct);
                        await orbitalElementsAccessor.Update(OrbitalObjectTypeEnum.Comet, elements.Response, progress, cts.Token);
                    } else {
                        var elements = await mpcAccessor.GetCometElements(ct);
                        await orbitalElementsAccessor.Update(OrbitalObjectTypeEnum.Comet, elements.Response, progress, cts.Token);
                    }
                    return true;
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception e) {
                    Logger.Error("Failed to update comet elements", e);
                    Notification.ShowError($"Failed to update comet elements. {e.Message}");
                    return false;
                }
            }, cts.Token);
            updateCometElementsTask = task;
            return task;
        }

        public Task<bool> ClearCometElements() {
            if (updateCometElementsTask != null && !updateCometElementsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            orbitalElementsAccessor.Clear(OrbitalObjectTypeEnum.Comet);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateNumberedAsteroids() {
            if (updateNumberedAsteroidsTask != null && !updateNumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateNumberedAsteroidsCts = cts;

            var task = Task.Run(async () => {
                try {
                    var ct = cts.Token;
                    var availableModifiedDate = await jplAccessor.GetNumberedAsteroidsLastModified(ct);
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.NumberedAsteroids);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.NumberedAsteroids.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetNumberedAsteroidElements(ct);
                    await orbitalElementsAccessor.Update(OrbitalObjectTypeEnum.NumberedAsteroids, elements.Response, progress, cts.Token);
                    return true;
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception e) {
                    Logger.Error("Failed to update comet elements", e);
                    Notification.ShowError($"Failed to update comet elements. {e.Message}");
                    return false;
                }
            }, cts.Token);
            updateNumberedAsteroidsTask = task;
            return task;
        }

        public Task<bool> ClearNumberedAsteroids() {
            if (updateNumberedAsteroidsTask != null && !updateNumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            orbitalElementsAccessor.Clear(OrbitalObjectTypeEnum.NumberedAsteroids);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateUnnumberedAsteroids() {
            if (updateUnnumberedAsteroidsTask != null && !updateUnnumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateUnnumberedAsteroidsCts = cts;

            var task = Task.Run(async () => {
                try {
                    var ct = cts.Token;
                    var availableModifiedDate = await jplAccessor.GetUnnumberedAsteroidsElementsLastModified(ct);
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.UnnumberedAsteroids);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.UnnumberedAsteroids.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetUnnumberedAsteroidElements(ct);
                    await orbitalElementsAccessor.Update(OrbitalObjectTypeEnum.UnnumberedAsteroids, elements.Response, progress, cts.Token);
                    return true;
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception e) {
                    Logger.Error("Failed to update comet elements", e);
                    Notification.ShowError($"Failed to update comet elements. {e.Message}");
                    return false;
                }
            }, cts.Token);
            updateUnnumberedAsteroidsTask = task;
            return task;
        }

        public Task<bool> ClearUnnumberedAsteroids() {
            if (updateUnnumberedAsteroidsTask != null && !updateUnnumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            orbitalElementsAccessor.Clear(OrbitalObjectTypeEnum.UnnumberedAsteroids);
            return Task.FromResult(true);
        }

        private int GetRefreshTime() {
            if (SearchObjectType == SearchObjectTypeEnum.ManualTLE) {
                return orbitalsOptions.TLEPositionRefreshTime_sec;
            } else {
                return orbitalsOptions.OrbitalPositionRefreshTime_sec;
            }
        }

        private CancellationTokenSource refreshCts;

        public void ToggleRefreshEnabled(bool status) {
            refreshCts?.Cancel();
            slewAndTrackCts?.Cancel();
            refreshCts = null;
            slewAndTrackCts = null;
            refreshTask = null;

            if (status) {
                var cts = new CancellationTokenSource();
                refreshCts = cts;

                int refreshTime = GetRefreshTime();
                refreshTask = Task.Run(async () => {
                    try {
                        var ct = cts.Token;
                        while (!ct.IsCancellationRequested) {
                            LoadSelection();
                            await Task.Delay(TimeSpan.FromSeconds(refreshTime), ct);
                        }
                        return true;
                    } catch (OperationCanceledException) {
                        RefreshEnabled = false;
                        refreshCts = null;
                        return false;
                    } catch (Exception e) {
                        Logger.Error("Failed to refresh orbital elements", e);
                        Notification.ShowError($"Failed to refresh orbital elements. {e.Message}");
                        RefreshEnabled = false;
                        refreshCts = null;
                        return false;
                    }
                }, cts.Token);
            }
            RefreshEnabled = status;
        }

        private async Task<bool> CancelUpdateElements(Task<bool> updateTask, CancellationTokenSource cts) {
            try {
                cts?.Cancel();
                if (updateTask != null) {
                    await updateTask;
                }
                return true;
            } catch (Exception) {
                return false;
            }
        }
    }
}