using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using Nito.Mvvm;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    public class OrbitalSearchVM : BaseINPC, IOrbitalSearchVM {

        private CancellationTokenSource targetSearchCts;
        private readonly IOrbitalElementsAccessor orbitalElementsAccessor;
        public OrbitalSearchVM(IOrbitalElementsAccessor orbitalElementsAccessor) {
            this.orbitalElementsAccessor = orbitalElementsAccessor;
        }

        private NotifyTask<List<IAutoCompleteItem>> targetSearchResult;

        public NotifyTask<List<IAutoCompleteItem>> TargetSearchResult {
            get {
                return targetSearchResult;
            }
            set {
                targetSearchResult = value;
                RaisePropertyChanged();
            }
        }

        public int Limit { get; set; } = 15;

        private bool SkipSearch { get; set; } = false;

        private OrbitalObjectType objectType;
        public OrbitalObjectType ObjectType {
            get => objectType;
            set {
                objectType = value;
                RaisePropertyChanged();
            }
        }

        private string targetName;

        public string TargetName {
            get => targetName;
            set {
                ShowPopup = false;
                targetName = value;
                if (!SkipSearch) {
                    if (TargetName.Length > 1) {
                        targetSearchCts?.Cancel();
                        targetSearchCts?.Dispose();
                        targetSearchCts = new CancellationTokenSource();

                        if (TargetSearchResult != null) {
                            TargetSearchResult.PropertyChanged -= TargetSearchResult_PropertyChanged;
                        }
                        TargetSearchResult = NotifyTask.Create(SearchObjects(ObjectType, TargetName, targetSearchCts.Token));
                        TargetSearchResult.PropertyChanged += TargetSearchResult_PropertyChanged;
                    }
                }
                RaisePropertyChanged();
            }
        }

        private void TargetSearchResult_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(TargetSearchResult.Result)) {
                if (targetSearchResult.Result.Count > 0) {
                    ShowPopup = true;
                } else {
                    ShowPopup = false;
                }
            }
        }

        public void SetTargetNameWithoutSearch(string targetName) {
            this.SkipSearch = true;
            this.TargetName = targetName;
            this.SkipSearch = false;
        }

        private OrbitalElements selectedOrbitalElements;

        public OrbitalElements SelectedOrbitalElements {
            get => selectedOrbitalElements;
            private set {
                selectedOrbitalElements = value;
                RaisePropertyChanged();
            }
        }

        private IAutoCompleteItem selectedTargetSearchResult;

        public IAutoCompleteItem SelectedTargetSearchResult {
            get {
                return selectedTargetSearchResult;
            }
            set {
                selectedTargetSearchResult = value;
                var orbitalObjectACI = value as OrbitalObjectAutoCompleteItem;
                if (orbitalObjectACI != null) {
                    this.SetTargetNameWithoutSearch(selectedTargetSearchResult.Column1);
                    SelectedOrbitalElements = orbitalObjectACI.Object;
                }
                RaisePropertyChanged();
            }
        }

        private bool showPopup;

        public bool ShowPopup {
            get {
                return showPopup;
            }
            set {
                showPopup = value;
                RaisePropertyChanged();
            }
        }

        private class OrbitalObjectAutoCompleteItem : IAutoCompleteItem {
            public string Column1 { get; set; }

            public string Column2 { get; set; }

            public string Column3 { get; set; }

            public OrbitalElements Object { get; set; }
        }

        private Task<List<IAutoCompleteItem>> SearchObjects(OrbitalObjectType objectType, string searchString, CancellationToken ct) {
            return Task.Run(async () => {
                await Task.Delay(100, ct);
                var results = this.orbitalElementsAccessor.Search(objectType, searchString).Take(Limit).ToList();
                var list = new List<IAutoCompleteItem>();
                foreach (var item in results) {
                    list.Add(new OrbitalObjectAutoCompleteItem() { Column1 = item.Name, Object = item });
                }
                return list;
            }, ct);
        }
    }
}
