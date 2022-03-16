#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    [Export(typeof(IDockableVM))]
    public class OrbitalsVM : DockableVM {
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;
        private readonly IJPLAccessor jplAccessor;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        private readonly IProgress<ApplicationStatus> progress;
        private bool initialLoadComplete;

        [ImportingConstructor]
        public OrbitalsVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator)
            : this(profileService, applicationStatusMediator, OrbitalsPlugin.OrbitalsOptions, OrbitalsPlugin.JPLAccessor, OrbitalsPlugin.OrbitalElementsAccessor, new OrbitalSearchVM(OrbitalsPlugin.OrbitalElementsAccessor)) {
        }

        public OrbitalsVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions,
            IJPLAccessor jplAccessor,
            IOrbitalElementsAccessor orbitalElementsAccessor,
            IOrbitalSearchVM orbitalSearchVM) : base(profileService) {
            this.Title = "Orbitals";

            /*
            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugin.Orbitals;component/Resources/SVGDataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["TenMicronSVG"];
            ImageGeometry.Freeze();
            */

            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;
            this.jplAccessor = jplAccessor;
            this.orbitalElementsAccessor = orbitalElementsAccessor;
            this.OrbitalSearchVM = orbitalSearchVM;
            this.progress = ProgressFactory.Create(applicationStatusMediator, "Orbitals");
            this.orbitalElementsAccessor.Updated += OrbitalElementsAccessor_Updated;
            var initialLoadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            Task.Run(async () => {
                try {
                    await orbitalElementsAccessor.Load(this.progress, initialLoadCts.Token);
                    initialLoadComplete = true;
                } catch (Exception e) {
                    Logger.Error("Initial orbital elements load failed", e);
                }
            });

            this.UpdateCometElementsCommand = new AsyncCommand<bool>(UpdateCometElements, (o) => initialLoadComplete);
            this.CancelUpdateCometElementsCommand = new AsyncCommand<bool>(o => CancelUpdateElements(updateCometElementsTask, updateCometElementsCts));
        }

        private void OrbitalElementsAccessor_Updated(object sender, OrbitalElementsObjectTypeUpdatedEventArgs e) {
            if (e.ObjectType == OrbitalObjectType.Comet) {
                CometCount = e.Count;
                CometLastUpdated = e.LastUpdated;
            }
        }

        private DateTime cometLastUpdated;
        public DateTime CometLastUpdated {
            get => cometLastUpdated;
            private set {
                cometLastUpdated = value;
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

        private SearchObjectTypeEnum searchObjectType = SearchObjectTypeEnum.SolarSystemBody;
        public SearchObjectTypeEnum SearchObjectType {
            get => searchObjectType;
            set {
                searchObjectType = value;
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

        public IOrbitalSearchVM OrbitalSearchVM { get; private set; }

        public ICommand CancelUpdateCometElementsCommand { get; private set; }

        public ICommand UpdateCometElementsCommand { get; private set; }

        public ICommand LoadSelectionCommand { get; private set; }

        private Task<bool> updateCometElementsTask;
        private CancellationTokenSource updateCometElementsCts;
        public Task<bool> UpdateCometElements(object o) {
            if (updateCometElementsCts != null) {
                Logger.Error("Update already in progress");
                return Task.FromResult(false);
            }

            var cts = new CancellationTokenSource();
            updateCometElementsCts = cts;

            var task = Task.Run(async () => {
                try {
                    var availableModifiedDate = await jplAccessor.GetCometElementsLastModified();
                    var localModifiedDate = orbitalElementsAccessor.GetLastUpdated(OrbitalObjectType.Comet);
                    if (availableModifiedDate < localModifiedDate) {
                        Notification.ShowInformation($"{OrbitalObjectType.Comet} elements already up to date");
                        return true;
                    }

                    var elements = await jplAccessor.GetCometElements();
                    await orbitalElementsAccessor.Update(OrbitalObjectType.Comet, elements.Response, progress, cts.Token);
                    return true;
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception e) {
                    Logger.Error("Failed to update comet elements", e);
                    Notification.ShowError($"Failed to update comet elements. {e.Message}");
                    return false;
                }
            }, updateCometElementsCts.Token);
            updateCometElementsTask = task;
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