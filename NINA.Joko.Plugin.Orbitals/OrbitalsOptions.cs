#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Enums;
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
            orbitalPositionRefreshTime_sec = optionsAccessor.GetValueInt32(nameof(OrbitalPositionRefreshTime_sec), 20);
            quirksMode = optionsAccessor.GetValueEnum(nameof(QuirksMode), QuirksModeEnum.None);
            cometAccessor = optionsAccessor.GetValueEnum(nameof(CometAccessor), OrbitalElementsAccessorEnum.MPC);
        }

        public void ResetDefaults() {
            OrbitalPositionRefreshTime_sec = 20;
            QuirksMode = QuirksModeEnum.None;
            CometAccessor = OrbitalElementsAccessorEnum.MPC;
        }

        private int orbitalPositionRefreshTime_sec;

        public int OrbitalPositionRefreshTime_sec {
            get => orbitalPositionRefreshTime_sec;
            set {
                if (orbitalPositionRefreshTime_sec != value) {
                    orbitalPositionRefreshTime_sec = value;
                    optionsAccessor.SetValueInt32(nameof(OrbitalPositionRefreshTime_sec), orbitalPositionRefreshTime_sec);
                    RaisePropertyChanged();
                }
            }
        }

        private QuirksModeEnum quirksMode;

        public QuirksModeEnum QuirksMode {
            get => quirksMode;
            set {
                if (quirksMode != value) {
                    quirksMode = value;
                    optionsAccessor.SetValueEnum(nameof(QuirksMode), quirksMode);
                    RaisePropertyChanged();
                }
            }
        }

        private OrbitalElementsAccessorEnum cometAccessor;

        public OrbitalElementsAccessorEnum CometAccessor {
            get => cometAccessor;
            set {
                if (cometAccessor != value) {
                    cometAccessor = value;
                    optionsAccessor.SetValueEnum(nameof(CometAccessor), cometAccessor);
                    RaisePropertyChanged();
                }
            }
        }
    }
}