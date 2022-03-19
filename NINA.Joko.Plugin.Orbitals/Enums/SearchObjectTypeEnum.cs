#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Converters;
using System;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Enums {

    [TypeConverter(typeof(EnumStaticDescriptionValueConverter))]
    public enum SearchObjectTypeEnum {

        [Description("Solar System Body")]
        SolarSystemBody = 0,

        [Description("Comet")]
        Comet = 1,

        [Description("Numbered Asteroids")]
        NumberedAsteroids = 2,

        [Description("Un-numbered Asteroids")]
        UnnumberedAsteroids = 3
    }

    public static class SearchObjectTypeEnumExtensions {

        public static OrbitalObjectTypeEnum ToOrbitalObjectTypeEnum(this SearchObjectTypeEnum objectType) {
            if (objectType == SearchObjectTypeEnum.SolarSystemBody) {
                throw new ArgumentException("SolarSystemBody cannot be converted to OrbitalObjectTypeEnum");
            }

            return (OrbitalObjectTypeEnum)objectType;
        }
    }
}