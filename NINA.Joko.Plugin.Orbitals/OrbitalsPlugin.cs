#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Properties;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using System.Windows.Input;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.Equipment.Interfaces;
using NINA.PlateSolving.Interfaces;

namespace NINA.Joko.Plugin.Orbitals {

    [Export(typeof(IPluginManifest))]
    public class OrbitalsPlugin : PluginBase {

        [ImportingConstructor]
        public OrbitalsPlugin(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                Settings.Default.Save();
            }

            if (OrbitalsOptions == null) {
                OrbitalsOptions = new OrbitalsOptions(profileService);
            }

            ResetOptionDefaultsCommand = new RelayCommand((object o) => OrbitalsOptions.ResetDefaults());
        }

        public static OrbitalsOptions OrbitalsOptions { get; private set; }

        public ICommand ResetOptionDefaultsCommand { get; private set; }
    }
}