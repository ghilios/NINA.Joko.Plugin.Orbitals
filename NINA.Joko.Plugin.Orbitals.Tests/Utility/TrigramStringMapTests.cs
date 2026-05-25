using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;
using System.Collections.Generic;
using System.Data.Linq;
using System.Linq;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture, Category("RequiresSqlite")]
    public class TrigramStringMapTests {

        private TrigramStringMap<string> sut;

        [SetUp]
        public void Setup() {
            sut = new TrigramStringMap<string>("test");
        }

        [TearDown]
        public void Teardown() {
            sut?.Dispose();
        }

        [Test]
        public void Add_IncrementsCount() {
            sut.Count.Should().Be(0);
            sut.Add("ceres", "1 Ceres");
            sut.Count.Should().Be(1);
        }

        [Test]
        public void Lookup_ExactMatch_ReturnsValue() {
            sut.Add("ceres", "1 Ceres");

            sut.Lookup("ceres").Should().Be("1 Ceres");
        }

        [Test]
        public void Lookup_NoMatch_ReturnsNull() {
            sut.Add("ceres", "1 Ceres");

            sut.Lookup("zzzz").Should().BeNull();
        }

        [Test]
        public void Lookup_AmbiguousSubstring_ThrowsDuplicateKeyException() {
            // Trigram match on "halley" hits both rows since both contain that substring.
            sut.Add("halley 1", "Halley 1");
            sut.Add("halley 2", "Halley 2");

            FluentActions.Invoking(() => sut.Lookup("halley"))
                .Should().Throw<DuplicateKeyException>();
        }

        [Test]
        public void Query_ReturnsAllMatchesUpToLimit() {
            sut.Add("alpha centauri", "v1");
            sut.Add("alpha romeo", "v2");
            sut.Add("beta cygni", "v3");

            var results = sut.Query("alpha", limit: null);

            results.Should().BeEquivalentTo(new[] { "v1", "v2" });
        }

        [Test]
        public void Query_LimitTruncatesResults() {
            sut.Add("alpha centauri", "v1");
            sut.Add("alpha romeo", "v2");

            sut.Query("alpha", limit: 1).Count.Should().Be(1);
        }

        [Test]
        public void AddRange_BulkInsertViaTransaction_PopulatesAll() {
            // The value-and-key indirection is real: AddRange takes a keyGetter that
            // pulls the search key out of each value. Here key == value for simplicity.
            var values = new[] { "alpha", "beta", "gamma" };

            sut.AddRange(v => v, values);

            sut.Count.Should().Be(3);
            sut.Lookup("alpha").Should().Be("alpha");
            sut.Lookup("beta").Should().Be("beta");
        }

        [Test]
        public void Enumeration_YieldsValuesInInsertionOrder() {
            sut.Add("z", "first");
            sut.Add("a", "second");
            sut.Add("m", "third");

            sut.ToList().Should().Equal("first", "second", "third");
        }

        [Test]
        public void QueryMatchingKeys_ReturnsKeyStringsNotValues() {
            sut.Add("alpha centauri", "v1");
            sut.Add("alpha romeo", "v2");

            var keys = sut.QueryMatchingKeys("alpha", limit: null);
            keys.Should().BeEquivalentTo(new[] { "alpha centauri", "alpha romeo" });
        }
    }
}
