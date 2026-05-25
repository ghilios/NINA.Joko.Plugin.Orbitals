using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;
using System.Collections.Generic;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture]
    public class ListExtensionsTests {

        // BinarySearchWithKey implementation details (Utility/ListExtensions.cs):
        //   - On a match, returns the matching index.
        //   - On a miss, the recursion eventually has rightIndex < leftIndex and
        //     returns the (smaller) rightIndex. So a not-found result is NOT the
        //     standard `~insertionPoint` convention -- it's whatever index the
        //     recursion narrowed down to, which can be any value in [-1, list.Count-1].
        //   - Therefore tests assert the *element at* the returned index, not the
        //     numeric index value, for not-found cases.

        private static List<int> Sorted(params int[] items) => new List<int>(items);

        [TestCase(10, 0)]
        [TestCase(30, 2)]
        [TestCase(50, 4)]
        public void Found_ReturnsMatchingIndex(int searchKey, int expectedIndex) {
            var list = Sorted(10, 20, 30, 40, 50);

            var idx = list.BinarySearchWithKey(x => x, searchKey);

            idx.Should().Be(expectedIndex);
        }

        [Test]
        public void SingleElement_Match_ReturnsZero() {
            var list = Sorted(42);

            list.BinarySearchWithKey(x => x, 42).Should().Be(0);
        }

        [Test]
        public void SingleElement_MissBelow_ReturnsNegativeOne() {
            // leftIndex=0, rightIndex=0; mid=0; middleKey=42 > 7 so recurse with rightIndex=-1
            // Next call: 0 > -1 is false, returns rightIndex (-1).
            var list = Sorted(42);

            list.BinarySearchWithKey(x => x, 7).Should().Be(-1);
        }

        [Test]
        public void SingleElement_MissAbove_ReturnsZero() {
            // leftIndex=0, rightIndex=0; mid=0; middleKey=42 < 100 so recurse with leftIndex=1
            // Next call: 0 >= 1 is false, returns rightIndex (0).
            var list = Sorted(42);

            list.BinarySearchWithKey(x => x, 100).Should().Be(0);
        }

        [Test]
        public void KeyExtractor_DerivesKeyFromComplexValue() {
            var rows = new List<(int Id, string Name)> {
                (10, "a"), (20, "b"), (30, "c")
            };

            rows.BinarySearchWithKey(r => r.Id, 20).Should().Be(1);
        }
    }
}
