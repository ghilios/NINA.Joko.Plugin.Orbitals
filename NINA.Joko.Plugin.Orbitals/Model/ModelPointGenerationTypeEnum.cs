#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.TenMicron.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugin.TenMicron.Model {

    [TypeConverter(typeof(EnumStaticDescriptionTypeConverter))]
    public enum ModelPointGenerationTypeEnum {

        [Description("Golden Spiral")]
        GoldenSpiral = 0,

        [Description("Sidereal Path")]
        SiderealPath = 1
    }
}