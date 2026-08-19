using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// End-to-end coverage of the merged comet source: two feeds are stored independently,
    /// the merge is derived at load, and provenance survives the round trip.
    ///
    /// The headline case is 220P/McNaught. JPL publishes it at a 2020 epoch, which
    /// two-body propagates to 84.5 arcminutes off its true position by 2026 -- far outside
    /// any field of view. MPC carries the current apparition. Under the merged source the
    /// user must get the MPC answer.
    /// </summary>
    [TestFixture, Category("RequiresSqlite")]
    public class MergedCometSourceTests {

        private static readonly PropertyInfo ElementsDirectoryProperty = typeof(OrbitalsPlugin)
            .GetProperty(nameof(OrbitalsPlugin.OrbitalElementsDirectory), BindingFlags.Public | BindingFlags.Static);

        private static readonly Angle GreenwichLat = Angle.ByDegree(51.4769);
        private static readonly Angle GreenwichLon = Angle.ByDegree(-0.0014);
        private const double GreenwichElev_m = 46.0;

        private string originalElementsDirectory;
        private string testElementsDirectory;
        private Mock<IOrbitalsOptions> optionsMock;
        private OrbitalElementsAccessor sut;

        [SetUp]
        public void Setup() {
            originalElementsDirectory = (string)ElementsDirectoryProperty.GetValue(null);
            testElementsDirectory = Path.Combine(Path.GetTempPath(), "OrbitalsMergeTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testElementsDirectory);
            ElementsDirectoryProperty.SetValue(null, testElementsDirectory);

            optionsMock = new Mock<IOrbitalsOptions>();
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPLAndMPC);
            sut = new OrbitalElementsAccessor(optionsMock.Object);
        }

        [TearDown]
        public void Teardown() {
            (sut as IDisposable)?.Dispose();
            ElementsDirectoryProperty.SetValue(null, originalElementsDirectory);
            if (Directory.Exists(testElementsDirectory)) {
                try { Directory.Delete(testElementsDirectory, recursive: true); } catch { /* best effort */ }
            }
        }

        private static StreamReader Reader(string text) =>
            new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        private void StoreJplFeed() {
            using var response = new JPLCometResponse(Reader(EmbeddedResources.ReadAllText("ELEMENTS.COMET.sample.txt")));
            sut.Update(OrbitalObjectTypeEnum.Comet, OrbitalElementsSourceEnum.JPL,
                response.Response.Cast<IOrbitalElementsSource>().ToList(), null, CancellationToken.None).Wait();
        }

        private void StoreMpcFeed(string importedFrom = null) {
            using var response = new MPCCometResponse(Reader(EmbeddedResources.ReadAllText("CometEls.sample.txt")));
            sut.Update(OrbitalObjectTypeEnum.Comet, OrbitalElementsSourceEnum.MPC,
                response.Response.Cast<IOrbitalElementsSource>().ToList(), importedFrom, null, CancellationToken.None).Wait();
        }

        // ===== The motivating case =====

        /// <summary>
        /// With both feeds stored, 220P must resolve to the MPC elements and land on the
        /// JPL Horizons position, not 84.5 arcminutes away from it.
        /// REF: JPL Horizons DES=220P; CAP&lt;2026-08-19;, topocentric ICRF from Greenwich,
        /// 2026-Aug-19 00:00 UT: RA 45.305221 deg, Dec +9.484476 deg.
        /// </summary>
        [Test]
        public void MergedSource_220P_MatchesHorizonsRatherThanTheStaleJplEntry() {
            StoreJplFeed();
            StoreMpcFeed();

            var elements = sut.Get(OrbitalObjectTypeEnum.Comet, "220P/McNaught");
            elements.Should().NotBeNull("the merged set must expose 220P under its plain name");
            elements.Source.Should().Be(OrbitalElementsSourceEnum.MPC);

            var pv = sut.GetObjectPV(
                new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc), elements,
                GreenwichLat, GreenwichLon, GreenwichElev_m, TimeSpan.FromSeconds(1));

            // 30 arcsec, matching the tolerance used by the other comet ephemeris tests.
            pv.Coordinates.RA.Should().BeApproximately(45.305221 / 15.0, 30.0 / 3600.0 / 15.0);
            pv.Coordinates.Dec.Should().BeApproximately(9.484476, 30.0 / 3600.0);
        }

        /// <summary>The same lookup under the JPL-only policy is badly wrong -- this is what the merge fixes.</summary>
        [Test]
        public void JplOnlySource_220P_IsFarFromHorizons() {
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            StoreJplFeed();

            var elements = sut.Get(OrbitalObjectTypeEnum.Comet, "220P/McNaught");
            elements.Should().NotBeNull();
            elements.Source.Should().Be(OrbitalElementsSourceEnum.JPL);

            var pv = sut.GetObjectPV(
                new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc), elements,
                GreenwichLat, GreenwichLon, GreenwichElev_m, TimeSpan.FromSeconds(1));

            var separationArcmin = SeparationArcsec(pv.Coordinates.RADegrees, pv.Coordinates.Dec, 45.305221, 9.484476) / 60.0;
            separationArcmin.Should().BeGreaterThan(30.0,
                "the stale JPL entry is the defect that motivated the merged source");
        }

        private static double SeparationArcsec(double ra1Deg, double dec1Deg, double ra2Deg, double dec2Deg) {
            var r1 = AstroUtil.ToRadians(ra1Deg);
            var d1 = AstroUtil.ToRadians(dec1Deg);
            var r2 = AstroUtil.ToRadians(ra2Deg);
            var d2 = AstroUtil.ToRadians(dec2Deg);
            var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
            return AstroUtil.ToDegree(Math.Acos(Math.Clamp(cosSep, -1.0, 1.0))) * 3600.0;
        }

        // ===== Per-feed stores =====

        [Test]
        public void MergedSource_CombinesCoverageFromBothFeeds() {
            StoreJplFeed();
            StoreMpcFeed();

            var merged = sut.GetCount(OrbitalObjectTypeEnum.Comet);

            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.MPC);
            var mpcOnly = new OrbitalElementsAccessor(optionsMock.Object);
            mpcOnly.Load(null, CancellationToken.None).Wait();

            merged.Should().BeGreaterThan(mpcOnly.GetCount(OrbitalObjectTypeEnum.Comet),
                "JPL contributes comets the MPC feed does not list");
        }

        /// <summary>
        /// Feeds are stored separately, so writing one must not disturb the other. This is
        /// what lets a failed download degrade a feed's age instead of removing it.
        /// </summary>
        [Test]
        public void StoringOneFeed_LeavesTheOtherIntact() {
            StoreJplFeed();
            StoreMpcFeed();
            var both = sut.GetCount(OrbitalObjectTypeEnum.Comet);

            // Re-store only JPL, as a second update would.
            StoreJplFeed();

            sut.GetCount(OrbitalObjectTypeEnum.Comet).Should().Be(both);
            sut.GetFeedMetadata(OrbitalObjectTypeEnum.Comet).Keys
               .Should().Contain(OrbitalElementsSourceEnum.MPC);
        }

        [Test]
        public void FeedMetadata_RecordsAnImportAndItsFileName() {
            StoreMpcFeed(importedFrom: "CometEls.txt");

            var feeds = sut.GetFeedMetadata(OrbitalObjectTypeEnum.Comet);
            feeds.Should().ContainKey(OrbitalElementsSourceEnum.MPC);

            var mpc = feeds[OrbitalElementsSourceEnum.MPC];
            mpc.Imported.Should().BeTrue();
            mpc.SourceFileName.Should().Be("CometEls.txt");
            mpc.RecordCount.Should().BeGreaterThan(0);
            mpc.DescribeAcquisition().Should().Contain("imported from CometEls.txt");
        }

        [Test]
        public void FeedMetadata_RecordsADownload() {
            StoreJplFeed();

            var jpl = sut.GetFeedMetadata(OrbitalObjectTypeEnum.Comet)[OrbitalElementsSourceEnum.JPL];
            jpl.Imported.Should().BeFalse();
            jpl.DescribeAcquisition().Should().StartWith("downloaded");
        }

        // ===== Provenance persistence =====

        [Test]
        public void Source_SurvivesAStoreAndReload() {
            StoreJplFeed();
            StoreMpcFeed();

            var reloaded = new OrbitalElementsAccessor(optionsMock.Object);
            reloaded.Load(null, CancellationToken.None).Wait();

            var elements = reloaded.Get(OrbitalObjectTypeEnum.Comet, "220P/McNaught");
            elements.Should().NotBeNull();
            elements.Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        /// <summary>
        /// Stores written before the Source field existed deserialize as Unknown. They must
        /// be stamped from the feed file they came out of, so existing installs show correct
        /// provenance without needing a re-download.
        /// </summary>
        [Test]
        public void Source_IsBackfilledForStoresWrittenBeforeTheFieldExisted() {
            var legacy = new Kepler.OrbitalElements("Legacy/Comet") {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                q_Perihelion_au = 1.0,
                e_Eccentricity = 0.5,
                i_Inclination_rad = 0,
                w_ArgOfPerihelion_rad = 0,
                node_LongitudeOfAscending_rad = 0,
                tp_PeriapsisTime_jd = 2451545.0
            };
            legacy.Source.Should().Be(OrbitalElementsSourceEnum.Unknown, "precondition");

            // Write it into the JPL feed store with the Source left unset, then reload.
            var path = Path.Combine(testElementsDirectory, "CometElements.bin.gz");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var gs = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal)) {
                ProtoBuf.Serializer.SerializeWithLengthPrefix(gs, legacy, ProtoBuf.PrefixStyle.Base128, 1);
            }

            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            var reloaded = new OrbitalElementsAccessor(optionsMock.Object);
            reloaded.Load(null, CancellationToken.None).Wait();

            reloaded.Get(OrbitalObjectTypeEnum.Comet, "Legacy/Comet")
                    .Source.Should().Be(OrbitalElementsSourceEnum.JPL);
        }

        // ===== Name disambiguation reaches the lookup =====

        /// <summary>
        /// When a merge has to append "(MPC)"/"(JPL)", a sequence saved against the bare name
        /// must still resolve. Without the fallback the target would silently fail to load.
        /// </summary>
        [Test]
        public void Get_FallsBackToTheDisambiguatedName() {
            // Both JPL rows reduce to key "100P", so the key is ambiguous and neither can be
            // matched against the MPC row. All three survive, and two of them share a name.
            var jpl = new[] { Synthetic("100P/Hartley", OrbitalElementsSourceEnum.JPL, 2450000.5),
                              Synthetic("100P/Hartley 1", OrbitalElementsSourceEnum.JPL, 2450001.5) };
            var mpc = new[] { Synthetic("100P/Hartley", OrbitalElementsSourceEnum.MPC, 2461271.5) };

            sut.Update(OrbitalObjectTypeEnum.Comet, OrbitalElementsSourceEnum.JPL, jpl, null, CancellationToken.None).Wait();
            sut.Update(OrbitalObjectTypeEnum.Comet, OrbitalElementsSourceEnum.MPC, mpc, null, CancellationToken.None).Wait();

            // Both survive under suffixed names...
            sut.Get(OrbitalObjectTypeEnum.Comet, "100P/Hartley (MPC)")
               .Source.Should().Be(OrbitalElementsSourceEnum.MPC);
            sut.Get(OrbitalObjectTypeEnum.Comet, "100P/Hartley (JPL)")
               .Source.Should().Be(OrbitalElementsSourceEnum.JPL);

            // ...and a sequence saved against the bare name still resolves, preferring MPC.
            var bare = sut.Get(OrbitalObjectTypeEnum.Comet, "100P/Hartley");
            bare.Should().NotBeNull("a target saved before the merge renamed it must still load");
            bare.Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        private static IOrbitalElementsSource Synthetic(string name, OrbitalElementsSourceEnum source, double epochJd) {
            return new SyntheticSource(new Kepler.OrbitalElements(name) {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = epochJd,
                Source = source,
                q_Perihelion_au = 1.5,
                e_Eccentricity = 0.5,
                i_Inclination_rad = 0.1,
                w_ArgOfPerihelion_rad = 0.2,
                node_LongitudeOfAscending_rad = 0.3,
                tp_PeriapsisTime_jd = epochJd + 10
            });
        }

        private sealed class SyntheticSource : IOrbitalElementsSource {
            private readonly Kepler.OrbitalElements elements;
            public SyntheticSource(Kepler.OrbitalElements elements) { this.elements = elements; }
            public string Name => elements.Name;
            public OrbitalElementsSourceEnum Source => elements.Source;
            public Kepler.OrbitalElements ToOrbitalElements() => elements;
        }
    }
}
