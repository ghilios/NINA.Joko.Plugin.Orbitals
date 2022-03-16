using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Converters;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Enums {

    [TypeConverter(typeof(EnumStaticDescriptionValueConverter))]
    public enum SolarSystemBody : short {
        [Description("Mercury")]
        Mercury = 1,

        [Description("Venus")]
        Venus = 2,

        [Description("Mars")]
        Mars = 4,

        [Description("Jupiter")]
        Jupiter = 5,

        [Description("Saturn")]
        Saturn = 6,

        [Description("Uranus")]
        Uranus = 7,

        [Description("Neptune")]
        Neptune = 8,

        [Description("Pluto")]
        Pluto = 9,

        [Description("Sun")]
        Sun = 10,

        [Description("Moon")]
        Moon = 11
    }

    public static class SolarSystemBodyExtensions {
        public static NOVAS.Body ToNOVAS(this SolarSystemBody body) {
            return (NOVAS.Body)body;
        }
    }
}
