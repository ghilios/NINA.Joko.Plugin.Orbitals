#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NINA.Astrometry;
using NINA.Astrometry.Interfaces;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Utility;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    [Export(typeof(IDockableVM))]
    public class OrbitalsVM : DockableVM {
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IGuiderMediator guiderMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IApplicationMediator applicationMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly IJPLAccessor jplAccessor;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IProgress<ApplicationStatus> progress;
        private bool initialLoadComplete;

        [ImportingConstructor]
        public OrbitalsVM(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IGuiderMediator guiderMediator,
            ITelescopeMediator telescopeMediator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IApplicationStatusMediator applicationStatusMediator)
            : this(profileService, nighttimeCalculator, guiderMediator, telescopeMediator, framingAssistantVM, applicationMediator, applicationStatusMediator, OrbitalsPlugin.OrbitalsOptions, OrbitalsPlugin.JPLAccessor, OrbitalsPlugin.OrbitalElementsAccessor, new OrbitalSearchVM(OrbitalsPlugin.OrbitalElementsAccessor)) {
        }

        public OrbitalsVM(
            IProfileService profileService,
            INighttimeCalculator nighttimeCalculator,
            IGuiderMediator guiderMediator,
            ITelescopeMediator telescopeMediator,
            IFramingAssistantVM framingAssistantVM,
            IApplicationMediator applicationMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions,
            IJPLAccessor jplAccessor,
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
            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;
            this.jplAccessor = jplAccessor;
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.OrbitalSearchVM = orbitalSearchVM;
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

            this.UpdateNumberedAsteroidElementsCommand = new AsyncRelayCommand(UpdateNumberedAsteroids, () => InitialLoadComplete);
            this.UpdateNumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.UpdateUnnumberedAsteroidElementsCommand = new AsyncRelayCommand(UpdateUnnumberedAsteroids, () => InitialLoadComplete);
            this.UpdateUnnumberedAsteroidElementsCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.UpdateJWSTVectorTableCommand = new AsyncRelayCommand(UpdateJWSTVectorTable, () => InitialLoadComplete);
            this.UpdateJWSTVectorTableCommand.RegisterPropertyChangeNotification(this, nameof(InitialLoadComplete));

            this.CancelUpdateCometElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateCometElementsTask, updateCometElementsCts));
            this.CancelUpdateNumberedAsteroidElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateNumberedAsteroidsTask, updateNumberedAsteroidsCts));
            this.CancelUpdateUnnumberedAsteroidElementsCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateUnnumberedAsteroidsTask, updateUnnumberedAsteroidsCts));
            this.CancelUpdateJWSTVectorTableCommand = new AsyncRelayCommand(o => CancelUpdateElements(updateJWSTVectorTableTask, updateJWSTVectorTableCts));

            this.LoadSelectionCommand = new RelayCommand(LoadSelection, CanLoad);
            this.LoadSelectionCommand.RegisterPropertyChangeNotification(this, nameof(SearchObjectType));
            this.LoadSelectionCommand.RegisterPropertyChangeNotification(OrbitalSearchVM, nameof(OrbitalSearchVM.TargetSearchResult));

            this.SendToFramingWizardCommand = new AsyncRelayCommand(SendToFramingWizardCommandAction, () => SelectedOrbitalsObject != null);
            this.SendToFramingWizardCommand.RegisterPropertyChangeNotification(this, nameof(SelectedOrbitalsObject));

            this.SetTrackingRateCommand = new RelayCommand(SetTrackingRateCommandAction, CanSetTrackingRate);
            this.SetTrackingRateCommand.RegisterPropertyChangeNotification(telescopeMediator.GetInfo(), nameof(TelescopeInfo.Connected), nameof(TelescopeInfo.CanSetRightAscensionRate), nameof(TelescopeInfo.CanSetDeclinationRate));

            this.SetGuiderShiftCommand = new AsyncRelayCommand(SetGuiderShiftCommandAction, CanSetGuiderShift);
            this.SetGuiderShiftCommand.RegisterPropertyChangeNotification(guiderMediator.GetInfo(), nameof(GuiderInfo.Connected), nameof(GuiderInfo.CanSetShiftRate));

            this.ResetOffsetCommand = new RelayCommand(ResetOffset, (o) => SelectedOrbitalsObject != null && (RAOffset != 0.0d || DecOffset != 0.0d));
            this.SetOffsetCommand = new RelayCommand(SetOffset, (o) => SelectedOrbitalsObject != null && telescopeMediator.GetInfo().Connected);
        }

        private Task<bool> SendToFramingWizardCommandAction() {
            return Task.Run(async () => {
                if (SelectedOrbitalsObject == null) {
                    Notification.ShowWarning("No orbital object selected");
                    return false;
                }

                try {
                    var adjustedCoordinates = TargetCoordinates.Clone();
                    adjustedCoordinates.RA += RAOffset;
                    adjustedCoordinates.Dec += DecOffset;

                    var dso = new DeepSkyObject(SelectedOrbitalsObject.Name, adjustedCoordinates.Transform(Epoch.J2000), profileService.ActiveProfile.ApplicationSettings.SkyAtlasImageRepository, profileService.ActiveProfile.AstrometrySettings.Horizon);
                    applicationMediator.ChangeTab(ApplicationTab.FRAMINGASSISTANT);
                    return await framingAssistantVM.SetCoordinates(dso);
                } catch (Exception e) {
                    Notification.ShowError($"Failed to send orbital target to framing wizard. {e.Message}");
                    Logger.Error("Failed to send orbital target to framing wizard", e);
                    return false;
                }
            });
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

        private bool CanSetTrackingRate() {
            var info = telescopeMediator.GetInfo();
            return info.Connected && info.CanSetRightAscensionRate && info.CanSetDeclinationRate;
        }

        private void SetTrackingRateCommandAction() {
            try {
                if (!this.telescopeMediator.SetCustomTrackingRate(ShiftTrackingRate.RAArcsecsPerSec, ShiftTrackingRate.DecArcsecsPerSec)) {
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

        private double distanceAU = 0.0;

        public double DistanceAU {
            get => distanceAU;
            private set {
                distanceAU = value;
                RaisePropertyChanged();
            }
        }

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

        public IOrbitalSearchVM OrbitalSearchVM { get; private set; }

        public AsyncRelayCommand CancelUpdateCometElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateCometElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateNumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateNumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateUnnumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand UpdateUnnumberedAsteroidElementsCommand { get; private set; }

        public AsyncRelayCommand CancelUpdateJWSTVectorTableCommand { get; private set; }

        public AsyncRelayCommand UpdateJWSTVectorTableCommand { get; private set; }

        public RelayCommand LoadSelectionCommand { get; private set; }

        public AsyncRelayCommand SlewCommand { get; private set; }

        public AsyncRelayCommand SendToFramingWizardCommand { get; private set; }

        public RelayCommand SetTrackingRateCommand { get; private set; }

        public AsyncRelayCommand SetGuiderShiftCommand { get; private set; }

        public ICommand ResetOffsetCommand { get; private set; }
        public ICommand SetOffsetCommand { get; private set; }

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
            } else {
                return OrbitalSearchVM.SelectedOrbitalElements != null;
            }
        }

        private void LoadSelection() {
            var objectType = SearchObjectType;
            try {
                NighttimeData = nighttimeCalculator.Calculate();
                if (objectType == SearchObjectTypeEnum.SolarSystemBody) {
                    LoadSolarSystemObject(SelectedSolarSystemBody);
                } else if (objectType == SearchObjectTypeEnum.JWST) {
                    LoadJWST();
                } else {
                    LoadOrbitalObject(OrbitalSearchVM.SelectedOrbitalElements);
                }

                TargetCoordinates = SelectedOrbitalsObject.Coordinates;
                ShiftTrackingRate = SelectedOrbitalsObject.ShiftTrackingRate;
                DistanceAU = SelectedOrbitalsObject.Position.Distance;
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

        private void ResetOffset(object o) {
            RAOffset = 0.0d;
            DecOffset = 0.0d;
        }

        private void SetOffset(object o) {
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
                    var vectorTable = await jplAccessor.GetJWSTVectorTable(DateTime.Now - TimeSpan.FromDays(1), TimeSpan.FromDays(8));
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
                    var availableModifiedDate = await jplAccessor.GetCometElementsLastModified();
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.Comet);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.Comet.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetCometElements();
                    await orbitalElementsAccessor.Update(OrbitalObjectTypeEnum.Comet, elements.Response, progress, cts.Token);
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

        public Task<bool> UpdateNumberedAsteroids() {
            if (updateNumberedAsteroidsTask != null && !updateNumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateNumberedAsteroidsCts = cts;

            var task = Task.Run(async () => {
                try {
                    var availableModifiedDate = await jplAccessor.GetNumberedAsteroidsLastModified();
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.NumberedAsteroids);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.NumberedAsteroids.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetNumberedAsteroidElements();
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

        public Task<bool> UpdateUnnumberedAsteroids() {
            if (updateUnnumberedAsteroidsTask != null && !updateUnnumberedAsteroidsTask.IsCompleted) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateUnnumberedAsteroidsCts = cts;

            var task = Task.Run(async () => {
                try {
                    var availableModifiedDate = await jplAccessor.GetUnnumberedAsteroidsElementsLastModified();
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectTypeEnum.UnnumberedAsteroids);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectTypeEnum.UnnumberedAsteroids.ToDescriptionString()} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetUnnumberedAsteroidElements();
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