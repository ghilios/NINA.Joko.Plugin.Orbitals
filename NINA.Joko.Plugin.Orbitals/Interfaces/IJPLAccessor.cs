#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Calculations;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Interfaces {

    public interface IJPLAccessor {

        Task<DateTime> GetCometElementsLastModified(CancellationToken ct);

        Task<DateTime> GetUnnumberedAsteroidsElementsLastModified(CancellationToken ct);

        Task<DateTime> GetNumberedAsteroidsLastModified(CancellationToken ct);

        Task<JPLCometResponse> GetCometElements(CancellationToken ct);

        Task<JPLNumberedAsteroidResponse> GetNumberedAsteroidElements(CancellationToken ct);

        Task<JPLUnnumberedAsteroidResponse> GetUnnumberedAsteroidElements(CancellationToken ct);

        Task<JPLVectorTable> GetJWSTVectorTable(DateTime asof, TimeSpan lookahead, CancellationToken ct);
    }
}