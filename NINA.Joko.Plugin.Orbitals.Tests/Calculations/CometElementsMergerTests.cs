using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Covers merging the JPL and MPC comet feeds.
    ///
    /// Two things here are correctness requirements rather than nice-to-haves:
    ///
    /// 1. The output must contain no duplicate Name. TrigramStringMap.Lookup throws
    ///    DuplicateKeyException on a duplicate and OrbitalElementsAccessor.Get turns that
    ///    into a null result, so a duplicate silently makes a comet unloadable.
    /// 2. Ambiguous designation keys (fragment families like 73P/Schwassmann-Wachmann 3-A)
    ///    must not be merged. Assigning a fragment's elements to its parent comet would put
    ///    the telescope somewhere entirely wrong, which is worse than using stale elements.
    /// </summary>
    [TestFixture]
    public class CometElementsMergerTests {

        private class FakeSource : IOrbitalElementsSource {

            public FakeSource(string name, OrbitalElementsSourceEnum source, double epochJd) {
                this.Name = name;
                this.Source = source;
                this.EpochJd = epochJd;
            }

            public string Name { get; }
            public OrbitalElementsSourceEnum Source { get; }
            public double EpochJd { get; }

            public Kepler.OrbitalElements ToOrbitalElements() {
                return new Kepler.OrbitalElements(Name) {
                    PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                    Epoch_jd = EpochJd,
                    Source = Source,
                    e_Eccentricity = 0.5,
                    q_Perihelion_au = 1.5,
                    i_Inclination_rad = 0.1,
                    w_ArgOfPerihelion_rad = 0.2,
                    node_LongitudeOfAscending_rad = 0.3,
                    tp_PeriapsisTime_jd = EpochJd + 10
                };
            }
        }

        private static FakeSource Jpl(string name, double epochJd) =>
            new FakeSource(name, OrbitalElementsSourceEnum.JPL, epochJd);

        private static FakeSource Mpc(string name, double epochJd) =>
            new FakeSource(name, OrbitalElementsSourceEnum.MPC, epochJd);

        // ===== Designation key normalization =====

        // The two feeds write the same comet differently, so the key has to see through it.
        [Test]
        [TestCase("220P/McNaught", "220P")]
        [TestCase("10P/Tempel", "10P")]
        [TestCase("10P/Tempel 2", "10P")]
        [TestCase("103P/Hartley", "103P")]
        [TestCase("103P/Hartley 2", "103P")]
        [TestCase("1P/Halley", "1P")]
        [TestCase("3D/Biela", "3D")]
        public void GetDesignationKey_NumberedPeriodic_IgnoresTrailingName(string name, string expected) {
            CometElementsMerger.GetDesignationKey(name).Should().Be(expected);
        }

        // MPC puts the fragment letter before the slash, JPL puts it at the end of the name.
        // Both must reduce to the same key, and crucially must NOT collide with the parent.
        [Test]
        [TestCase("141P-A/Machholz", "141P-A")]
        [TestCase("73P/Schwassmann-Wachmann 3-A", "73P-A")]
        [TestCase("73P/Schwassmann-Wachmann 3", "73P")]
        [TestCase("51P/Harrington-A", "51P-A")]
        [TestCase("51P/Harrington", "51P")]
        public void GetDesignationKey_Fragments_HandledInBothPositions(string name, string expected) {
            CometElementsMerger.GetDesignationKey(name).Should().Be(expected);
        }

        [Test]
        [TestCase("C/2021 Y1 (ATLAS)", "C/2021 Y1")]
        [TestCase("C/1995 O1 (Hale-Bopp)", "C/1995 O1")]
        [TestCase("P/1996 R2 (Lagerkvist)", "P/1996 R2")]
        [TestCase("C/2002 CE10 (LINEAR)", "C/2002 CE10")]
        [TestCase("C/1882 R1-A (Great September comet)", "C/1882 R1-A")]
        public void GetDesignationKey_ProvisionalDesignations(string name, string expected) {
            CometElementsMerger.GetDesignationKey(name).Should().Be(expected);
        }

        [Test]
        [TestCase("1I/`Oumuamua", "1I")]
        [TestCase("2I/Borisov", "2I")]
        public void GetDesignationKey_InterstellarObjects(string name, string expected) {
            CometElementsMerger.GetDesignationKey(name).Should().Be(expected);
        }

        // ===== Selection =====

        [Test]
        public void Merge_PrefersTheNewerEpoch() {
            var result = CometElementsMerger.Merge(
                new[] { Jpl("220P/McNaught", 2459114.5) },
                new[] { Mpc("220P/McNaught", 2461271.5) });

            result.Elements.Should().HaveCount(1);
            result.Elements[0].Source.Should().Be(OrbitalElementsSourceEnum.MPC);
            result.Elements[0].Epoch_jd.Should().Be(2461271.5);
            result.FromMPC.Should().Be(1);
            result.FromJPL.Should().Be(0);
        }

        [Test]
        public void Merge_PrefersJplWhenJplIsNewer() {
            var result = CometElementsMerger.Merge(
                new[] { Jpl("1P/Halley", 2461271.5) },
                new[] { Mpc("1P/Halley", 2459114.5) });

            result.Elements.Should().HaveCount(1);
            result.Elements[0].Source.Should().Be(OrbitalElementsSourceEnum.JPL);
            result.FromJPL.Should().Be(1);
        }

        /// <summary>MPC republishes every comet at a common current epoch, so it is the better bet on a tie.</summary>
        [Test]
        public void Merge_TiesGoToMpc() {
            var result = CometElementsMerger.Merge(
                new[] { Jpl("2P/Encke", 2461271.5) },
                new[] { Mpc("2P/Encke", 2461271.5) });

            result.Elements.Should().HaveCount(1);
            result.Elements[0].Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        // The whole point of merging: MPC's freshness plus JPL's coverage.
        [Test]
        public void Merge_KeepsEntriesPresentInOnlyOneFeed() {
            var result = CometElementsMerger.Merge(
                new[] { Jpl("C/1977 V1 (Tsuchinshan)", 2443000.5), Jpl("220P/McNaught", 2459114.5) },
                new[] { Mpc("3I/ATLAS", 2461271.5), Mpc("220P/McNaught", 2461271.5) });

            result.Elements.Select(e => e.Name).Should().BeEquivalentTo(new[] {
                "C/1977 V1 (Tsuchinshan)", "220P/McNaught", "3I/ATLAS"
            });
            result.Total.Should().Be(3);
        }

        [Test]
        public void Merge_MatchesAcrossDifferingNames() {
            // MPC drops the discriminator that JPL carries; these are the same comet.
            var result = CometElementsMerger.Merge(
                new[] { Jpl("10P/Tempel 2", 2450000.5) },
                new[] { Mpc("10P/Tempel", 2461271.5) });

            result.Elements.Should().HaveCount(1, "the two names denote the same comet");
            result.Elements[0].Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        // ===== Ambiguity safety =====

        /// <summary>
        /// Fragments must key distinctly from their parent comet, so a fragment never
        /// absorbs (or is absorbed by) the parent during a merge. Getting this wrong would
        /// point the telescope at a different body entirely.
        /// </summary>
        [Test]
        public void Merge_FragmentsDoNotCollideWithTheirParent() {
            var jpl = new[] {
                Jpl("73P/Schwassmann-Wachmann 3", 2450000.5),
                Jpl("73P/Schwassmann-Wachmann 3-B", 2450000.5),
                Jpl("73P/Schwassmann-Wachmann 3-C", 2450000.5)
            };
            var mpc = new[] { Mpc("73P/Schwassmann-Wachmann 3", 2461271.5) };

            var result = CometElementsMerger.Merge(jpl, mpc);

            // JPL yields distinct keys 73P, 73P-B and 73P-C, so the parent merges with the
            // MPC row while both fragments survive untouched.
            result.Elements.Should().HaveCount(3);
            result.Elements.Single(e => e.Name == "73P/Schwassmann-Wachmann 3")
                  .Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        [Test]
        public void Merge_TrulyAmbiguousKeyIsLeftUnmerged() {
            // Two JPL rows reduce to the same key, so no safe match exists.
            var jpl = new[] {
                Jpl("100P/Hartley", 2450000.5),
                Jpl("100P/Hartley 1", 2450001.5)
            };
            var mpc = new[] { Mpc("100P/Hartley", 2461271.5) };

            var result = CometElementsMerger.Merge(jpl, mpc);

            result.Total.Should().Be(3, "an ambiguous key must not consume either candidate");
            result.Elements.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        }

        // ===== Name uniqueness =====

        /// <summary>
        /// When both feeds retain a record under the same name, the source is appended so the
        /// user can tell them apart and so TrigramStringMap never sees a duplicate key.
        /// </summary>
        [Test]
        public void Merge_AppendsSourceToDisambiguateCollidingNames() {
            var jpl = new[] { Jpl("100P/Hartley", 2450000.5), Jpl("100P/Hartley 1", 2450001.5) };
            var mpc = new[] { Mpc("100P/Hartley", 2461271.5) };

            var result = CometElementsMerger.Merge(jpl, mpc);

            result.Elements.Select(e => e.Name).Should().Contain("100P/Hartley (JPL)");
            result.Elements.Select(e => e.Name).Should().Contain("100P/Hartley (MPC)");
            result.Elements.Select(e => e.Name).Should().NotContain("100P/Hartley");
            result.Disambiguated.Should().Be(2);
        }

        [Test]
        public void Merge_OutputNamesAreAlwaysUnique() {
            var jpl = new[] { Jpl("X/Same", 1.0), Jpl("X/Other", 2.0) };
            var mpc = new[] { Mpc("X/Same", 3.0), Mpc("X/Other", 4.0) };

            var result = CometElementsMerger.Merge(jpl, mpc);

            result.Elements.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void Merge_HandlesNullFeeds() {
            var onlyJpl = CometElementsMerger.Merge(new[] { Jpl("1P/Halley", 1.0) }, null);
            onlyJpl.Total.Should().Be(1);
            onlyJpl.FromJPL.Should().Be(1);

            var onlyMpc = CometElementsMerger.Merge(null, new[] { Mpc("1P/Halley", 1.0) });
            onlyMpc.Total.Should().Be(1);
            onlyMpc.FromMPC.Should().Be(1);

            CometElementsMerger.Merge(null, null).Total.Should().Be(0);
        }

        // ===== Against the real published files =====

        private static StreamReader Reader(string text) =>
            new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        private static List<IOrbitalElementsSource> LoadJplSample() {
            using var response = new JPLCometResponse(Reader(EmbeddedResources.ReadAllText("ELEMENTS.COMET.sample.txt")));
            return response.Response.Cast<IOrbitalElementsSource>().ToList();
        }

        private static List<IOrbitalElementsSource> LoadMpcSample() {
            using var response = new MPCCometResponse(Reader(EmbeddedResources.ReadAllText("CometEls.sample.txt")));
            return response.Response.Cast<IOrbitalElementsSource>().ToList();
        }

        /// <summary>
        /// The duplicate-name guard, exercised against real rows from both published files
        /// rather than synthetic ones. This is the assertion that protects Get() from
        /// returning null for a comet that is actually present.
        /// </summary>
        [Test]
        public void Merge_RealSampleFiles_ProduceNoDuplicateNames() {
            var result = CometElementsMerger.Merge(LoadJplSample(), LoadMpcSample());

            result.Total.Should().BeGreaterThan(0);
            result.Elements.Select(e => e.Name).Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void Merge_RealSampleFiles_PrefersMpcForOverlappingComets() {
            var jpl = LoadJplSample();
            var mpc = LoadMpcSample();
            var result = CometElementsMerger.Merge(jpl, mpc);

            // 220P/McNaught is the case that motivated this work: JPL publishes it at a 2020
            // epoch, which propagates to 84.5 arcmin off by 2026. MPC must win.
            var mcnaught = result.Elements.Single(e => e.Name.StartsWith("220P"));
            mcnaught.Source.Should().Be(OrbitalElementsSourceEnum.MPC);

            // And the merge must not have lost JPL-only coverage.
            result.Total.Should().BeGreaterThan(mpc.Count,
                "JPL contributes comets the MPC file does not list");
        }

        [Test]
        public void Merge_RealSampleFiles_EveryElementCarriesItsSource() {
            var result = CometElementsMerger.Merge(LoadJplSample(), LoadMpcSample());

            result.Elements.Should().OnlyContain(e => e.Source != OrbitalElementsSourceEnum.Unknown);
            (result.FromJPL + result.FromMPC).Should().Be(result.Total);
        }
    }
}
