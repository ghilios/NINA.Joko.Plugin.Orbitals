using FluentAssertions;
using Moq;
using NINA.Astrometry;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NUnit.Framework;
using System;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Ephemeris accuracy tests for the major solar system bodies, mirroring what
    /// <see cref="AsteroidEphemerisTests"/> does for the orbital-elements path.
    ///
    /// These exist because <see cref="OrbitalElementsAccessor.GetSolarSystemBodyPV"/> used
    /// to return a GEOCENTRIC apparent place (NOVAS app_planet). For the Moon that is
    /// wrong by up to ~1 degree of diurnal parallax -- 48 arcmin at the epoch used below,
    /// which puts the target well outside any telescope FOV. The Moon cases here are the
    /// regression guard for that; the outer planets would not have caught it, since their
    /// parallax is only ~1 arcsec.
    ///
    /// Reference values are JPL Horizons "R.A.___(ICRF)___DEC" (astrometric ICRS) for a
    /// TOPOCENTRIC center at Greenwich, which is the convention GetSolarSystemBodyPV
    /// returns (Epoch.J2000) and the same convention the asteroid/comet suites use.
    ///
    /// Measured residual is under 0.05 arcsec for every case; the 5 arcsec tolerance is
    /// headroom for a different JPL ephemeris kernel (Horizons used DE441).
    ///
    /// References for all data:
    ///   JPL Horizons system, https://ssd.jpl.nasa.gov/horizons/ - captured 2026-08-19.
    ///   CENTER='coord@399' COORD_TYPE=GEODETIC SITE_COORD='-0.0014,51.4769,0.046'
    /// </summary>
    [TestFixture, Category("RequiresSqlite")]
    public class SolarSystemBodyEphemerisTests {

        private static readonly Angle GreenwichLat = Angle.ByDegree(51.4769);
        private static readonly Angle GreenwichLon = Angle.ByDegree(-0.0014);
        private const double GreenwichElev_m = 46.0;

        private const double Tolerance_Arcsec = 5.0;

        private OrbitalElementsAccessor sut;

        [SetUp]
        public void Setup() {
            var optionsMock = new Mock<IOrbitalsOptions>();
            optionsMock.SetupGet(o => o.CometAccessor).Returns(OrbitalElementsAccessorEnum.JPL);
            sut = new OrbitalElementsAccessor(optionsMock.Object);
        }

        [TearDown]
        public void Teardown() {
            (sut as IDisposable)?.Dispose();
        }

        private static double SeparationArcsec(double ra1Deg, double dec1Deg, double ra2Deg, double dec2Deg) {
            var r1 = AstroUtil.ToRadians(ra1Deg);
            var d1 = AstroUtil.ToRadians(dec1Deg);
            var r2 = AstroUtil.ToRadians(ra2Deg);
            var d2 = AstroUtil.ToRadians(dec2Deg);
            var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
            return AstroUtil.ToDegree(Math.Acos(Math.Clamp(cosSep, -1.0, 1.0))) * 3600.0;
        }

        private OrbitalPositionVelocity Observe(DateTime asof, SolarSystemBody body) {
            return sut.GetSolarSystemBodyPV(
                asof, body, GreenwichLat, GreenwichLon, GreenwichElev_m, TimeSpan.FromSeconds(1));
        }

        private void AssertAstrometricRaDec(
            SolarSystemBody body, DateTime asof, double expectedRaDeg, double expectedDecDeg) {
            var pv = Observe(asof, body);

            pv.Coordinates.Epoch.Should().Be(Epoch.J2000);
            SeparationArcsec(pv.Coordinates.RADegrees, pv.Coordinates.Dec, expectedRaDeg, expectedDecDeg)
                .Should().BeLessThan(Tolerance_Arcsec,
                    "{0} at {1:u} should match the JPL Horizons topocentric astrometric place", body, asof);
        }

        // REF: Horizons OBSERVER, 2026-Aug-19 00:00 UT, topocentric at Greenwich.
        private static readonly DateTime Epoch2026 = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Moon_TopocentricAstrometricRaDecFromGreenwich() {
            // The headline regression case. A geocentric implementation lands at
            // RA 219.119171, Dec -20.628787 -- 2916 arcsec (48.6') away from this.
            AssertAstrometricRaDec(SolarSystemBody.Moon, Epoch2026,
                expectedRaDeg: 218.536943971, expectedDecDeg: -21.229102197);
        }

        [Test]
        public void Sun_TopocentricAstrometricRaDecFromGreenwich() {
            AssertAstrometricRaDec(SolarSystemBody.Sun, Epoch2026,
                expectedRaDeg: 147.945861420, expectedDecDeg: 12.953849817);
        }

        [Test]
        public void Venus_TopocentricAstrometricRaDecFromGreenwich() {
            AssertAstrometricRaDec(SolarSystemBody.Venus, Epoch2026,
                expectedRaDeg: 189.855860130, expectedDecDeg: -6.222824713);
        }

        [Test]
        public void Mars_TopocentricAstrometricRaDecFromGreenwich() {
            AssertAstrometricRaDec(SolarSystemBody.Mars, Epoch2026,
                expectedRaDeg: 95.088168534, expectedDecDeg: 23.695793993);
        }

        [Test]
        public void Jupiter_TopocentricAstrometricRaDecFromGreenwich() {
            AssertAstrometricRaDec(SolarSystemBody.Jupiter, Epoch2026,
                expectedRaDeg: 133.144328157, expectedDecDeg: 18.076134344);
        }

        [Test]
        public void Saturn_TopocentricAstrometricRaDecFromGreenwich() {
            AssertAstrometricRaDec(SolarSystemBody.Saturn, Epoch2026,
                expectedRaDeg: 13.822899736, expectedDecDeg: 3.088962996);
        }

        // The Moon's parallax swings with hour angle, so a single-instant test could in
        // principle be satisfied by a wrong-but-coincidentally-close implementation.
        // These sample the same night at 4-hour intervals.
        // REF: Horizons, topocentric at Greenwich, 2026-Aug-19.
        [Test]
        [TestCase(4, 221.028832102, -21.752046618)]
        [TestCase(8, 223.670701511, -22.437912933)]
        [TestCase(12, 225.931670258, -23.250078934)]
        [TestCase(16, 227.630870502, -24.000687584)]
        [TestCase(20, 229.135777967, -24.517021031)]
        public void Moon_TracksParallaxAcrossTheNight(int hourOfDay, double expectedRaDeg, double expectedDecDeg) {
            AssertAstrometricRaDec(SolarSystemBody.Moon,
                new DateTime(2026, 8, 19, hourOfDay, 0, 0, DateTimeKind.Utc),
                expectedRaDeg, expectedDecDeg);
        }

        /// <summary>
        /// The shift tracking rate is differenced from two positions, so a geocentric
        /// implementation gets the Moon's rate wrong as well as its position: it misses
        /// the diurnal parallax rate, which is worth about -239"/hr in RA and -176"/hr in
        /// Dec at this epoch. That is roughly 0.08 arcsec/sec of uncorrected drift.
        /// </summary>
        [Test]
        public void Moon_TrackingRateIncludesDiurnalParallax() {
            // REF: Horizons topocentric astrometric places at 00:00 and 01:00 UT differ by
            // RA +0.579957341 deg and Dec -0.120576371 deg, i.e. a mean rate over that hour
            // of 2087.8"/hr and -434.1"/hr. Sampled at 00:30 so the plugin's instantaneous
            // rate is centred on the same interval -- the Moon's RA rate curves by ~6% per
            // hour here, so an end-anchored comparison would not be meaningful.
            // A geocentric implementation returns 1851"/hr and -607"/hr instead.
            const double expectedRaArcsecPerSec = 2087.8 / 3600.0;
            const double expectedDecArcsecPerSec = -434.1 / 3600.0;
            const double relativeTolerance = 0.02;

            var pv = Observe(Epoch2026.AddMinutes(30), SolarSystemBody.Moon);

            pv.TrackingRate.Enabled.Should().BeTrue();
            pv.TrackingRate.RAArcsecsPerSec.Should().BeApproximately(
                expectedRaArcsecPerSec, Math.Abs(expectedRaArcsecPerSec * relativeTolerance));
            pv.TrackingRate.DecArcsecsPerSec.Should().BeApproximately(
                expectedDecArcsecPerSec, Math.Abs(expectedDecArcsecPerSec * relativeTolerance));
        }

        /// <summary>
        /// Distance is surfaced in the UI (OrbitalsVM) and by OrbitalsContainerBase, and
        /// should be the topocentric range, not the geocentric one. For the Moon the two
        /// differ by 0.74%.
        /// </summary>
        [Test]
        public void Moon_DistanceIsTopocentric() {
            // REF: Horizons "delta" for a topocentric center = 0.00267891194869 au.
            // The geocentric value is 0.00265924889909 au.
            var pv = Observe(Epoch2026, SolarSystemBody.Moon);

            pv.Position.Distance.Should().BeApproximately(0.00267891194869, 1e-6);
        }
    }
}
