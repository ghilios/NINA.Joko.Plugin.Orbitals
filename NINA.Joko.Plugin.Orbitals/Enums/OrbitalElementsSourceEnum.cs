#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Enums {

    /// <summary>
    /// Which published dataset a single object's elements came from. This is per-object
    /// provenance, and is deliberately distinct from <see cref="OrbitalElementsAccessorEnum"/>,
    /// which is the user's source *policy* (and can be "Both").
    ///
    /// There is no "user file" value: an imported file supplies the data for the JPL or MPC
    /// feed, so it changes how a feed was acquired, not which dataset an object came from.
    /// Acquisition is tracked per feed in <see cref="OrbitalElementsFeedMetadata"/>.
    /// </summary>
    [TypeConverter(typeof(EnumStaticDescriptionValueConverter))]
    public enum OrbitalElementsSourceEnum {

        [Description("Unknown")]
        Unknown = 0,

        [Description("JPL")]
        JPL = 1,

        [Description("MPC")]
        MPC = 2
    }
}
