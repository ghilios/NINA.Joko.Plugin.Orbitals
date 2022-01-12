#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NINA.Joko.Plugin.Orbitals {

    public class OrbitalsOptions : BaseINPC, IOrbitalsOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public OrbitalsOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(OrbitalsOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            InitializeOptions();
        }

        private void InitializeOptions() {
            goldenSpiralStarCount = optionsAccessor.GetValueInt32("GoldenSpiralStarCount", 9);
        }

        public void ResetDefaults() {
            GoldenSpiralStarCount = 9;
        }

        private int goldenSpiralStarCount;

        public int GoldenSpiralStarCount {
            get => goldenSpiralStarCount;
            set {
                if (goldenSpiralStarCount != value) {
                    goldenSpiralStarCount = value;
                    optionsAccessor.SetValueInt32("GoldenSpiralStarCount", goldenSpiralStarCount);
                    RaisePropertyChanged();
                }
            }
        }
    }
}