using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {
    public interface IOrbitalElementsSource {
        string Name { get; }
        OrbitalElements ToOrbitalElements();
    }
}
