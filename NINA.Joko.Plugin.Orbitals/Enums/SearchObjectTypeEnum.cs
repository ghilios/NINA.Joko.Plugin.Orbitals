using NINA.Joko.Plugin.Orbitals.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Enums {
    [TypeConverter(typeof(EnumStaticDescriptionValueConverter))]
    public enum SearchObjectTypeEnum {
        [Description("Solar System Body")]
        SolarSystemBody = 0,

        [Description("Comet")]
        Comet = 1
    }
}
