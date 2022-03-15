using NINA.Joko.Plugin.Orbitals.Calculations;
using System;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {
    public interface IJPLAccessor {
        Task<DateTime> GetCometElementsLastModified();
        Task<JPLCometResponse> GetCometElements();
    }
}
