using NINA.Core.Interfaces;
using Nito.Mvvm;
using System.Collections.Generic;
using System.ComponentModel;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {
    public interface IOrbitalSearchVM : INotifyPropertyChanged {
        NotifyTask<List<IAutoCompleteItem>> TargetSearchResult { get; set; }
        int Limit { get; set; }
        OrbitalObjectType ObjectType { get; set; }
        string TargetName { get; set; }
        void SetTargetNameWithoutSearch(string targetName);
        OrbitalElements SelectedOrbitalElements { get; }
        IAutoCompleteItem SelectedTargetSearchResult { get; set; }
        bool ShowPopup { get; set; }
    }
}
