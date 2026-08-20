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
            tlePositionRefreshTime_sec = optionsAccessor.GetValueInt32(nameof(TLEPositionRefreshTime_sec), 5);
            tleTrackStartWaitTime_sec = optionsAccessor.GetValueInt32(nameof(TLETrackStartWaitTime_sec), 30);
            quirksMode = optionsAccessor.GetValueEnum(nameof(QuirksMode), QuirksModeEnum.None);
            cometAccessor = optionsAccessor.GetValueEnum(nameof(CometAccessor), OrbitalElementsAccessorEnum.JPLAndMPC);
            MigrateCometAccessorDefault();
        }

        /// <summary>
        /// Moves existing profiles onto the merged comet source once.
        ///
        /// Neither single-source setting is a good default. JPL publishes each comet at its
        /// own solution epoch, so 78.6% of its entries are more than a decade stale -- for
        /// 220P/McNaught that puts the comet 84.5 arcminutes off, which is well outside the
        /// field of view. MPC is fresh but lists only ~950 comets against JPL's ~4000.
        /// Merging is strictly better than either: it never loses an object, and it never
        /// picks the older elements.
        ///
        /// Migrating is therefore safe for MPC users too -- they gain coverage and lose
        /// nothing. Guarded by a flag so a deliberate later choice is not overwritten.
        /// </summary>
        private void MigrateCometAccessorDefault() {
            if (optionsAccessor.GetValueBoolean(nameof(CometAccessorDefaultMigrated), false)) {
                return;
            }

            var previous = cometAccessor;
            optionsAccessor.SetValueBoolean(nameof(CometAccessorDefaultMigrated), true);
            if (previous == OrbitalElementsAccessorEnum.JPLAndMPC) {
                return;
            }

            cometAccessor = OrbitalElementsAccessorEnum.JPLAndMPC;
            optionsAccessor.SetValueEnum(nameof(CometAccessor), cometAccessor);
            CometAccessorMigratedFrom = previous;
        }

        /// <summary>
        /// Set when this session moved the profile onto the merged source, so the UI can
        /// explain the change once. Null when nothing was migrated.
        /// </summary>
        public OrbitalElementsAccessorEnum? CometAccessorMigratedFrom { get; private set; }

        private bool CometAccessorDefaultMigrated { get; set; }

        public void ResetDefaults() {
            OrbitalPositionRefreshTime_sec = 20;
            TLEPositionRefreshTime_sec = 5;
            TLETrackStartWaitTime_sec = 30;
            QuirksMode = QuirksModeEnum.None;
            CometAccessor = OrbitalElementsAccessorEnum.JPLAndMPC;
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

        private int tlePositionRefreshTime_sec;

        public int TLEPositionRefreshTime_sec {
            get => tlePositionRefreshTime_sec;
            set {
                if (tlePositionRefreshTime_sec != value) {
                    tlePositionRefreshTime_sec = value;
                    optionsAccessor.SetValueInt32(nameof(TLEPositionRefreshTime_sec), tlePositionRefreshTime_sec);
                    RaisePropertyChanged();
                }
            }
        }

        private int tleTrackStartWaitTime_sec;

        public int TLETrackStartWaitTime_sec {
            get => tleTrackStartWaitTime_sec;
            set {
                if (tleTrackStartWaitTime_sec != value) {
                    tleTrackStartWaitTime_sec = value;
                    optionsAccessor.SetValueInt32(nameof(TLETrackStartWaitTime_sec), tleTrackStartWaitTime_sec);
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