using NINA.Joko.Plugin.Orbitals.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {
    public interface IOrbitalElementsSource {
        string Name { get; }

        /// <summary>
        /// Which published dataset this row came from. Flows through to
        /// <see cref="OrbitalElements.Source"/> so the origin of a loaded object stays
        /// visible after the elements are merged and persisted.
        /// </summary>
        OrbitalElementsSourceEnum Source { get; }

        OrbitalElements ToOrbitalElements();
    }
}
