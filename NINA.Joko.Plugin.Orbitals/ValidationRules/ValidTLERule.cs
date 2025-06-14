#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Utility;
using System;
using System.Globalization;
using System.Windows.Controls;

namespace NINA.Joko.Plugin.Orbitals.ValidationRules {

    public class ValidTLERule : ValidationRule {

        public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
            if (value is null) {
                return new ValidationResult(false, "Null value");
            }
            try {
                TleUtil.ParseTle(value.ToString());
                return new ValidationResult(true, null);
            } catch (Exception e) {
                return new ValidationResult(false, e.Message);
            }
        }
    }
}