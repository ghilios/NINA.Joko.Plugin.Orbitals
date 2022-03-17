#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Joko.Plugin.Orbitals.Enums;
using Nito.Mvvm;
using System.Collections.Generic;
using System.ComponentModel;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {

    public interface IOrbitalSearchVM : INotifyPropertyChanged {
        NotifyTask<List<IAutoCompleteItem>> TargetSearchResult { get; set; }
        int Limit { get; set; }
        OrbitalObjectTypeEnum ObjectType { get; set; }
        string TargetName { get; set; }

        void SetTargetNameWithoutSearch(string targetName);

        OrbitalElements SelectedOrbitalElements { get; }
        IAutoCompleteItem SelectedTargetSearchResult { get; set; }
        bool ShowPopup { get; set; }
    }
}