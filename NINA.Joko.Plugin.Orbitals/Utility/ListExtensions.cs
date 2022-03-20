#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class ListExtensions {

        public static int BinarySearchWithKey<T, K>(this IList<T> list, Func<T, K> keyGetter, K key) where K : IComparable<K> {
            return BinarySearchWithKey(list, keyGetter, key, 0, list.Count - 1);
        }

        private static int BinarySearchWithKey<T, K>(this IList<T> list, Func<T, K> keyGetter, K key, int leftIndex, int rightIndex) where K : IComparable<K> {
            if (rightIndex >= leftIndex) {
                int mid = leftIndex + (rightIndex - leftIndex) / 2;
                K middleKey = keyGetter(list[mid]);

                if (middleKey.CompareTo(key) == 0) {
                    return mid;
                }

                if (middleKey.CompareTo(key) > 0) {
                    return BinarySearchWithKey(list, keyGetter, key, leftIndex, mid - 1);
                }
                return BinarySearchWithKey(list, keyGetter, key, mid + 1, rightIndex);
            }
            return rightIndex;
        }
    }
}