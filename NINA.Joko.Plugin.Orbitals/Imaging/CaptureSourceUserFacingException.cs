#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugin.Orbitals.Imaging {

    /// <summary>
    /// Thrown by an <see cref="ICaptureSource"/> implementation when a pre-flight check
    /// fails and the user has already been notified via <c>Notification.ShowError</c>.
    /// The VM's catch block should handle this silently (log only) to avoid showing a
    /// second duplicate notification.
    /// </summary>
    internal class CaptureSourceUserFacingException : Exception {
        public CaptureSourceUserFacingException(string message) : base(message) { }
    }
}
