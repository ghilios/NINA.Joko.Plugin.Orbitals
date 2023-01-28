#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Globalization;
using System.Windows.Controls;

namespace NINA.Joko.Plugin.Orbitals.ValidationRules {

    public class HoursExRule : ValidationRule {

        public override ValidationResult Validate(object value, CultureInfo cultureInfo) {
            if (int.TryParse(value.ToString(), NumberStyles.Integer, cultureInfo, out var intval)) {
                if (intval <= -24) {
                    return new ValidationResult(false, "Value must be greater than -24");
                } else if (intval >= 24) {
                    return new ValidationResult(false, "Value must be less than 24");
                } else {
                    return new ValidationResult(true, null);
                }
            } else {
                return new ValidationResult(false, "Invalid value");
            }
        }
    }
}