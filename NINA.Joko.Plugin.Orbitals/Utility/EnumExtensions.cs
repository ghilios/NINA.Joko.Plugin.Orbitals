#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class EnumExtensions {

        public static string ToDescriptionString(this Enum value) {
            var fi = value?.GetType().GetField(value.ToString());
            if (fi != null) {
                var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);
                var label = ((attributes.Length > 0) && (!String.IsNullOrEmpty(attributes[0].Description))) ? attributes[0].Description : value.ToString();
                if (label.StartsWith("Lbl")) {
                    return global::NINA.Core.Locale.Loc.Instance[label];
                } else {
                    return label;
                }
            }
            return value.ToString();
        }
    }
}