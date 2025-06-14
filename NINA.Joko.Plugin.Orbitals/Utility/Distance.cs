#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugin.Orbitals.Calculations;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public class Distance : BaseINPC {

        public Distance(double au) {
            this.au = au;
        }

        private double au;

        public double AU {
            get => au;
            set {
                if (au != value) {
                    this.au = value;
                    RaiseAllPropertiesChanged();
                }
            }
        }

        public double DisplayDistance {
            get {
                if (au < 0.1) {
                    return au * AstrometricConstants.KM_PER_AU;
                } else {
                    return au;
                }
            }
        }

        public string DisplayUnits {
            get {
                if (au < 0.1) {
                    return "km";
                } else {
                    return "au";
                }
            }
        }
    }
}