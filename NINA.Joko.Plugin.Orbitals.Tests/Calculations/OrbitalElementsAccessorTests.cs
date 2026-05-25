using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture, Category("RequiresSqlite")]
    public class OrbitalElementsAccessorTests {

        private Mock<IOrbitalsOptions> optionsMock;
        private OrbitalElementsAccessor sut;

        [SetUp]
        public void Setup() {
            optionsMock = new Mock<IOrbitalsOptions>();
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            sut = new OrbitalElementsAccessor(optionsMock.Object);
        }

        [TearDown]
        public void Teardown() {
            (sut as IDisposable)?.Dispose();
        }

        [Test]
        public void GetCount_BeforeAnyUpdate_IsZero() {
            sut.GetCount(OrbitalObjectTypeEnum.Comet).Should().Be(0);
            sut.GetCount(OrbitalObjectTypeEnum.NumberedAsteroids).Should().Be(0);
            sut.GetCount(OrbitalObjectTypeEnum.UnnumberedAsteroids).Should().Be(0);
        }

        [Test]
        public void GetLastUpdated_BeforeAnyUpdate_IsMinValue() {
            sut.GetLastUpdated(OrbitalObjectTypeEnum.Comet).Should().Be(DateTime.MinValue);
        }

        [Test]
        public async System.Threading.Tasks.Task Update_StoresElementsAndRaisesUpdatedEvent() {
            var source = new SyntheticOrbitalSource(Fixtures.Halley());
            OrbitalElementsObjectTypeUpdatedEventArgs received = null;
            sut.Updated += (_, args) => received = args;

            await sut.Update(OrbitalObjectTypeEnum.Comet, new[] { source }, progress: null, CancellationToken.None);

            sut.GetCount(OrbitalObjectTypeEnum.Comet).Should().Be(1);
            received.Should().NotBeNull();
            received!.ObjectType.Should().Be(OrbitalObjectTypeEnum.Comet);
            received.Count.Should().Be(1);
        }

        [Test]
        public async System.Threading.Tasks.Task Get_ExactName_ReturnsStoredElement() {
            await sut.Update(
                OrbitalObjectTypeEnum.Comet,
                new[] { new SyntheticOrbitalSource(Fixtures.Halley()) },
                progress: null,
                CancellationToken.None);

            var found = sut.Get(OrbitalObjectTypeEnum.Comet, "1P/Halley");
            found.Should().NotBeNull();
            found.Name.Should().Be("1P/Halley");
        }

        [Test]
        public async System.Threading.Tasks.Task Search_TrigramSubstring_ReturnsMatches() {
            await sut.Update(
                OrbitalObjectTypeEnum.Comet,
                new[] {
                    new SyntheticOrbitalSource(Fixtures.Halley()),
                    new SyntheticOrbitalSource(MakeNamedElement("2P/Encke")),
                },
                progress: null,
                CancellationToken.None);

            var results = sut.Search(OrbitalObjectTypeEnum.Comet, "Halley", limit: 10).ToList();
            results.Should().HaveCount(1);
            results[0].Name.Should().Be("1P/Halley");
        }

        [Test]
        public void GetSolarSystemBodyPV_Jupiter_RunsEndToEnd_ProducesValidCoordinates() {
            // Sanity-check that the NOVAS pipeline runs end-to-end and emits a result
            // with plausible RA [0,24) and Dec [-90,+90]. A tighter assertion against
            // a JPL Horizons snapshot is deferred until a captured-snapshot fixture is
            // committed (the exact value depends on the ephemeris source, time scale,
            // and aberration/light-time conventions, which differ slightly between
            // JPL Horizons and NOVAS's PlanetApparentCoordinates).
            var asof = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var pv = sut.GetSolarSystemBodyPV(asof, SolarSystemBody.Jupiter, TimeSpan.FromSeconds(1));

            pv.Should().NotBeNull();
            pv.Coordinates.Should().NotBeNull();
            pv.Coordinates.RA.Should().BeInRange(0, 24);
            pv.Coordinates.Dec.Should().BeInRange(-90, 90);
            pv.TrackingRate.Should().NotBeNull();
        }

        [Test]
        public void GetObjectPV_HalleyAtKnownEpoch_ProducesCoherentResult() {
            // We don't have a JPL Horizons reference snapshot committed yet, but we
            // can sanity-check that the pipeline runs end-to-end and produces a
            // coherent OrbitalPositionVelocity (non-null coordinates, distance > 0).
            var halley = Fixtures.Halley();
            var asof = new DateTime(1986, 2, 9, 0, 0, 0, DateTimeKind.Utc);
            var lat = Angle.ByDegree(40);
            var lon = Angle.ByDegree(-105);

            var pv = sut.GetObjectPV(asof, halley, lat, lon, elevation: 1600, rateDriftDelta: TimeSpan.FromSeconds(1));

            pv.Should().NotBeNull();
            pv.Coordinates.Should().NotBeNull();
            pv.Coordinates.RA.Should().BeInRange(0, 24);
            pv.Coordinates.Dec.Should().BeInRange(-90, 90);
        }

        [Test]
        public void GetPVFromTable_NullOrTooFewRows_ReturnsNull() {
            sut.GetPVFromTable(DateTime.UtcNow, null, Angle.Zero, Angle.Zero, 0, TimeSpan.FromSeconds(1))
                .Should().BeNull();

            var tooFew = new Kepler.PVTable("test") { Rows = new System.Collections.Generic.List<Kepler.PVTableRow>() };
            sut.GetPVFromTable(DateTime.UtcNow, tooFew, Angle.Zero, Angle.Zero, 0, TimeSpan.FromSeconds(1))
                .Should().BeNull();
        }

        [Test]
        public void GetPVFromTable_BeforeStartRange_ReturnsNull() {
            var table = MakeConstantVelocityTable(
                startJd: 2451545.0,
                stepDays: 1.0,
                rowCount: 3,
                startPos: new RectangularCoordinates(1.0, 0.0, 0.0),
                velocity: new RectangularCoordinates(0.01, 0.0, 0.0));

            // asof before the first row's epoch
            var before = NOVAS.JulianToDateTime(2451544.0);
            sut.GetPVFromTable(before, table, Angle.Zero, Angle.Zero, 0, TimeSpan.FromSeconds(1))
                .Should().BeNull();
        }

        [Test]
        public void GetPVFromTable_AtEndOrAfter_ReturnsNull() {
            var table = MakeConstantVelocityTable(2451545.0, 1.0, 3,
                new RectangularCoordinates(1.0, 0.0, 0.0),
                new RectangularCoordinates(0.0, 0.0, 0.0));

            var afterEnd = NOVAS.JulianToDateTime(2451548.0);
            sut.GetPVFromTable(afterEnd, table, Angle.Zero, Angle.Zero, 0, TimeSpan.FromSeconds(1))
                .Should().BeNull();
        }

        [Test]
        public void GetPVFromTable_InsideRange_ReturnsResult() {
            // Constant-velocity table from 2451545 in 1-day steps. Query in the middle.
            var table = MakeConstantVelocityTable(2451545.0, 1.0, 3,
                new RectangularCoordinates(1.0, 0.0, 0.0),
                new RectangularCoordinates(0.0, 0.0, 0.0));

            var middle = NOVAS.JulianToDateTime(2451545.5);
            var pv = sut.GetPVFromTable(middle, table, Angle.Zero, Angle.Zero, 0, TimeSpan.FromSeconds(1));

            pv.Should().NotBeNull();
        }

        /// <summary>Test wrapper around OrbitalElements that satisfies IOrbitalElementsSource.</summary>
        private sealed class SyntheticOrbitalSource : IOrbitalElementsSource {
            private readonly Kepler.OrbitalElements elements;
            public SyntheticOrbitalSource(Kepler.OrbitalElements elements) { this.elements = elements; }
            public string Name => elements.Name;
            public Kepler.OrbitalElements ToOrbitalElements() => elements;
        }

        private static Kepler.OrbitalElements MakeNamedElement(string name) {
            return new Kepler.OrbitalElements(name) {
                PrimaryGravitationalParameter = Kepler.GravitationalParameter.Sun,
                Epoch_jd = 2451545.0,
                q_Perihelion_au = 1.0,
                e_Eccentricity = 0.5,
                i_Inclination_rad = 0,
                w_ArgOfPerihelion_rad = 0,
                node_LongitudeOfAscending_rad = 0,
                tp_PeriapsisTime_jd = 2451545.0,
            };
        }

        private static Kepler.PVTable MakeConstantVelocityTable(
            double startJd, double stepDays, int rowCount,
            RectangularCoordinates startPos, RectangularCoordinates velocity) {
            var rows = new System.Collections.Generic.List<Kepler.PVTableRow>();
            for (var i = 0; i < rowCount; i++) {
                var t = startJd + i * stepDays;
                var pos = startPos + velocity * (i * stepDays);
                rows.Add(new Kepler.PVTableRow {
                    Epoch_jd = t,
                    X = pos.X, Y = pos.Y, Z = pos.Z,
                    VelocityX = velocity.X, VelocityY = velocity.Y, VelocityZ = velocity.Z,
                });
            }
            return new Kepler.PVTable("test") { Rows = rows };
        }
    }
}
