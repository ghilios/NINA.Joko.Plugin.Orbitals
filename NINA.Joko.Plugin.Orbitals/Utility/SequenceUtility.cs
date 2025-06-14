#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class SequenceUtility {

        public static T GetParent<T>(ISequenceContainer container) where T : ISequenceContainer {
            if (container == null) {
                return default(T);
            }
            if (container is T typedContainer) {
                return typedContainer;
            }
            return GetParent<T>(container.Parent);
        }

        public static T GetParent<T>(ISequenceItem item) where T : ISequenceContainer {
            return GetParent<T>(item.Parent);
        }
    }
}