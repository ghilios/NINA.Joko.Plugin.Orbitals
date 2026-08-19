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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture, Category("RequiresSqlite")]
    public class OrbitalElementsAccessorTests {

        // OrbitalElementsAccessor.Update serializes to a file under
        // OrbitalsPlugin.OrbitalElementsDirectory. Without isolation, tests would
        // (a) fail on a fresh CI runner because that directory doesn't exist, and
        // (b) clobber a dev machine's real NINA comet/asteroid bundle. Redirect the
        // path to a per-fixture temp directory via the private setter (reflection)
        // and restore on teardown.
        private static readonly PropertyInfo ElementsDirectoryProperty = typeof(OrbitalsPlugin)
            .GetProperty(nameof(OrbitalsPlugin.OrbitalElementsDirectory), BindingFlags.Public | BindingFlags.Static);

        private string originalElementsDirectory;
        private string testElementsDirectory;

        private Mock<IOrbitalsOptions> optionsMock;
        private OrbitalElementsAccessor sut;

        [SetUp]
        public void Setup() {
            originalElementsDirectory = (string)ElementsDirectoryProperty.GetValue(null);
            testElementsDirectory = Path.Combine(Path.GetTempPath(), "OrbitalsTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testElementsDirectory);
            ElementsDirectoryProperty.SetValue(null, testElementsDirectory);

            optionsMock = new Mock<IOrbitalsOptions>();
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            sut = new OrbitalElementsAccessor(optionsMock.Object);
        }

        [TearDown]
        public void Teardown() {
            (sut as IDisposable)?.Dispose();
            ElementsDirectoryProperty.SetValue(null, originalElementsDirectory);
            if (Directory.Exists(testElementsDirectory)) {
                try { Directory.Delete(testElementsDirectory, recursive: true); } catch { /* best-effort */ }
            }
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
            // with plausible RA [0,24) and Dec [-90,+90]. Accuracy against JPL Horizons
            // is asserted in SolarSystemBodyEphemerisTests.
            var asof = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            var pv = sut.GetSolarSystemBodyPV(
                asof, SolarSystemBody.Jupiter,
                Angle.ByDegree(51.4769), Angle.ByDegree(-0.0014), 46.0,
                TimeSpan.FromSeconds(1));

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

        // ===== End-to-end: JWST vector-table -> apparent RA/Dec =====
        //
        // Verifies the ecl<->equ rotation signs in OrbitalElementsAccessor.cs:390/395
        // (one of the historically-flagged "untested code paths"). The plugin's normal
        // JWST ingestion fetches Horizons VECTORS with CENTER='500@399' (geocenter)
        // which returns J2000 mean ecliptic vectors; the test feeds the same data and
        // verifies the resulting RA/Dec matches Horizons' apparent observer ephemeris.
        //
        // REF: JPL Horizons VECTORS query, COMMAND='JWST' CENTER='500@399'
        //     OUT_UNITS='AU-D'  (default REF_PLANE is Ecliptic of J2000.0).
        // REF: JPL Horizons OBSERVER query for the midpoint epoch,
        //     CENTER='coord@399' SITE_COORD='-0.0014,51.4769,0.046' (Greenwich).
        // Both captured 2026-05-24.
        [Test]
        public void GetPVFromTable_JWSTGeocentricVectorsAtGreenwich_MatchHorizonsRaDec() {
            // Three consecutive 1-hour rows around 2024-Jan-01 00:00 - 02:00 TDB.
            var table = new Kepler.PVTable("JWST") {
                Rows = new System.Collections.Generic.List<Kepler.PVTableRow> {
                    new Kepler.PVTableRow {
                        Epoch_jd = 2460310.500000000,
                        X = -1.117092746928064e-03, Y = 1.095216677746271e-02, Z = -2.840454907141245e-03,
                        VelocityX = -2.649577414946749e-05, VelocityY = -9.771258107124170e-06, VelocityZ = -3.448520341331241e-07,
                    },
                    new Kepler.PVTableRow {
                        Epoch_jd = 2460310.541666667,
                        X = -1.118196161613144e-03, Y = 1.095175900962256e-02, Z = -2.840467100268888e-03,
                        VelocityX = -2.646813485318297e-05, VelocityY = -9.801528123503693e-06, VelocityZ = -2.404141625778554e-07,
                    },
                    new Kepler.PVTableRow {
                        Epoch_jd = 2460310.583333333,
                        X = -1.119298425560640e-03, Y = 1.095134998931084e-02, Z = -2.840474941158609e-03,
                        VelocityX = -2.644054274147435e-05, VelocityY = -9.831376905624774e-06, VelocityZ = -1.359443997800955e-07,
                    },
                },
            };

            // Midpoint: 2024-Jan-01 00:30 UT (~ midway through the first interval).
            var asof = new DateTime(2024, 1, 1, 0, 30, 0, DateTimeKind.Utc);
            var pv = sut.GetPVFromTable(
                asof, table,
                Angle.ByDegree(51.4769), Angle.ByDegree(-0.0014), elevation: 46.0,
                TimeSpan.FromSeconds(1));

            pv.Should().NotBeNull();
            // REF: Horizons ICRF RA/Dec at 2024-Jan-01 00:30 UT from Greenwich.
            //   RA = 95.67694 deg = 6.378463 hours, Dec = +8.71090 deg.
            // Tolerance 30 arcsec: GetPVFromTable does interpolation between hourly rows
            // and does NOT apply light-time (light-time to L2 is ~5 seconds, sub-arcsec
            // shift). The dominant residual is the difference between Horizons'
            // light-time-corrected geocentric position and the table's instantaneous
            // interpolation.
            // Tolerance 60 arcsec. Measured residual is ~23 arcsec in RA. The dominant
            // error source is GetPVFromTable not applying light-time correction (unlike
            // GetObjectPV which was updated to do single-iteration light-time): the
            // table is interpolated at observer time, but Horizons' ICRS column is the
            // geocentric position as seen at observer time, i.e. JWST's position at
            // observer_time - distance/c. JWST's ~5.6 s light-time corresponds to
            // Earth moving ~168 km, which shifts JWST's apparent geocentric direction
            // by ~20 arcsec at its 1.7e6 km distance. Acceptable here -- the test's
            // purpose is the +/- sign convention of the ecliptic/equatorial rotations
            // at lines 390 and 395, which would manifest as a ~47 deg Dec offset if wrong.
            pv.Coordinates.RA.Should().BeApproximately(95.67694 / 15.0, 60.0 / 3600.0 / 15.0);
            pv.Coordinates.Dec.Should().BeApproximately(8.71090, 60.0 / 3600.0);
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
