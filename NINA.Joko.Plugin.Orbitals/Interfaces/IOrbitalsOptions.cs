#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Enums;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {

    public interface IOrbitalsOptions : INotifyPropertyChanged {
        int OrbitalPositionRefreshTime_sec { get; set; }

        int TLEPositionRefreshTime_sec { get; set; }

        int TLETrackStartWaitTime_sec { get; set; }

        QuirksModeEnum QuirksMode { get; set; }

        OrbitalElementsAccessorEnum CometAccessor { get; set; }

        void ResetDefaults();
    }
}