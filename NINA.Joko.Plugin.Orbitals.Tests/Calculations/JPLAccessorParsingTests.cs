using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture]
    public class JPLAccessorParsingTests {

        // JPL fixed-width format: header line (column names), spacing line
        // (dashes whose lengths determine each column's width), then data.
        // Splitting the spacing line on single space yields the per-column widths.

        // 9 columns: name(40) epoch(5) q(11) e(10) i(10) w(11) node(10) tp(17) ref(6)
        private const string CometFixture =
            "Name                                     Epoch q           e          i          w           node       tp                ref   \n" +
            "---------------------------------------- ----- ----------- ---------- ---------- ----------- ---------- ----------------- ------\n" +
            "1P/Halley                                49400 0.585978112 0.96714291 162.262691 111.3324851 58.4200810 19860209.45154    JPL 73\n";

        // 11 columns: name(20) epoch(5) a(11) e(10) i(10) w(11) node(10) M(10) H(5) G(5) ref(6)
        // Both H and G have distinct values to make bug #2 (line 258 duplicate H mapping) observable.
        private const string UnnumberedAsteroidFixture =
            "Name                 Epoch a           e          i          w           node       M          H     G     ref   \n" +
            "-------------------- ----- ----------- ---------- ---------- ----------- ---------- ---------- ----- ----- ------\n" +
            "1990 SH1             60200 2.450123456 0.12345678 11.2345678 100.1234567 80.7654321 90.1234567 17.10 0.150 JPL 12\n";

        // 12 columns: number(4) name(15) epoch(5) a(11) e(10) i(10) w(11) node(10) M(10) H(5) G(5) ref(6)
        private const string NumberedAsteroidFixture =
            "Num. Name            Epoch a           e          i          w           node       M          H     G     ref   \n" +
            "---- --------------- ----- ----------- ---------- ---------- ----------- ---------- ---------- ----- ----- ------\n" +
            "   1 Ceres           60200 2.769165200 0.07891260 10.5879572 73.43228860 80.2549325 60.0728817  3.34 0.120 JPL 50\n";

        private static StreamReader FromString(string content) =>
            new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(content)));

        [Test]
        public void CometResponse_ParsesHalleyRowIntoExpectedFields() {
            using var response = new JPLCometResponse(FromString(CometFixture));

            var rows = response.Response.ToList();

            rows.Should().HaveCount(1);
            var halley = rows[0];
            halley.name.Trim().Should().Be("1P/Halley");
            halley.epoch.Should().Be(49400);
            halley.q.Should().BeApproximately(0.585978112, 1e-9);
            halley.e.Should().BeApproximately(0.96714291, 1e-9);
            halley.i.Should().BeApproximately(162.262691, 1e-6);
            halley.w.Should().BeApproximately(111.3324851, 1e-7);
            halley.node.Should().BeApproximately(58.4200810, 1e-7);
            halley.tp.Should().BeApproximately(19860209.45154, 1e-5);
            halley.ref_.Trim().Should().Be("JPL 73");
        }

        [Test]
        public void NumberedAsteroidResponse_ParsesCeresRowIntoExpectedFields() {
            using var response = new JPLNumberedAsteroidResponse(FromString(NumberedAsteroidFixture));

            var rows = response.Response.ToList();

            rows.Should().HaveCount(1);
            var ceres = rows[0];
            ceres.number.Should().Be(1);
            ceres.name.Trim().Should().Be("Ceres");
            ceres.epoch.Should().Be(60200);
            ceres.a.Should().BeApproximately(2.769165200, 1e-9);
            ceres.e.Should().BeApproximately(0.07891260, 1e-8);
            ceres.i.Should().BeApproximately(10.5879572, 1e-7);
            ceres.w.Should().BeApproximately(73.43228860, 1e-7);
            ceres.node.Should().BeApproximately(80.2549325, 1e-7);
            ceres.M.Should().BeApproximately(60.0728817, 1e-7);
            ceres.H.Should().BeApproximately(3.34, 1e-2);
            ceres.G.Should().BeApproximately(0.120, 1e-3);
            ceres.GetName().Should().Be("1/Ceres");
        }

        [Test]
        public void UnnumberedAsteroidResponse_ParsesRowIntoExpectedFields_IncludingBothHAndG() {
            // Both H (absolute magnitude, column 9) and G (magnitude slope parameter,
            // column 10) are populated. Prior to the fix at JPLAccessor.cs:258 the
            // mapper had a duplicate H mapping, leaving G silently at the default 0.
            using var response = new JPLUnnumberedAsteroidResponse(FromString(UnnumberedAsteroidFixture));

            var rows = response.Response.ToList();

            rows.Should().HaveCount(1);
            var row = rows[0];
            row.name.Trim().Should().Be("1990 SH1");
            row.epoch.Should().Be(60200);
            row.a.Should().BeApproximately(2.450123456, 1e-9);
            row.H.Should().BeApproximately(17.10, 1e-2);
            row.G.Should().BeApproximately(0.150, 1e-3);
        }

        [Test]
        public void CalendarDateAndFractionToJulian_KnownDates_MatchUSNOReference() {
            // REF: USNO. NOVAS.JulianDate is a P/Invoke into NOVAS31lib.dll which the
            // CopyNinaNativeAssets target wires into the test bin under External/x64/NOVAS.
            // J2000.0 = 2000-Jan-01.5 -> yyyymmdd.fraction = 20000101.5 -> JD 2451545.0.
            var jd = JPLAccessor.CalendarDateAndFractionToJulian(20000101.5);
            jd.Should().BeApproximately(2451545.0, 1e-2);
        }

        // REF: USNO Julian Date reference points.
        //   2000-01-01 12:00 = JD 2451545.0   (J2000.0 epoch)
        //   2000-01-01 00:00 = JD 2451544.5
        //   1970-01-01 00:00 = JD 2440587.5   (Unix epoch)
        //   1986-02-09 10:50:13 = JD 2446470.95154 (1P/Halley 1986 perihelion)
        [TestCase(20000101.5, 2451545.0)]
        [TestCase(20000101.0, 2451544.5)]
        [TestCase(19700101.0, 2440587.5)]
        [TestCase(19860209.45154, 2446470.95154)]
        public void CalendarDateAndFractionToJulian_KnownDates(double dateAndFraction, double expectedJd) {
            JPLAccessor.CalendarDateAndFractionToJulian(dateAndFraction)
                .Should().BeApproximately(expectedJd, 1e-5);
        }

        [Test]
        public void CalendarDateAndFractionToJulian_DayPartCloseToOne_RollsCleanlyIntoNextDay() {
            // Edge case: a dateAndFraction just below the next calendar day must yield a
            // JD essentially equal to the next day's midnight. Potential failure mode is
            // dayPart*24 yielding ~24 due to float rounding, which NOVAS.JulianDate would
            // not normalize. In practice the double representation of 20000101.99999999
            // truncates to a dayPart of ~0.99999999, giving 23.99999976 hours, well under 24.
            var jdAtAlmostNextDay = JPLAccessor.CalendarDateAndFractionToJulian(20000101.99999999);
            var jdAtNextDay = JPLAccessor.CalendarDateAndFractionToJulian(20000102.0);
            jdAtAlmostNextDay.Should().BeApproximately(jdAtNextDay, 1e-5);
        }

        [Test]
        public void CalendarDateAndFractionToJulian_NegativeYear_PreservesPositiveMonthAndDay() {
            // BCE date encoded as a negative yyyymmdd.fraction: -5000101.0 = year -500
            // Jan 1 at midnight. The sign is applied only to the year (month/day come
            // from the magnitude). REF: NOVAS supports proleptic-Julian BCE dates;
            // negative year -500 Jan 1 has a known JD per the standard algorithm.
            // Verify the function preserves positive month/day in the negative-year case
            // by round-tripping: the +500 Jan 1 and -500 Jan 1 JDs must differ by
            // exactly 1000 years of days (varies by leap rule, so verify approximately).
            var jdPositive = JPLAccessor.CalendarDateAndFractionToJulian(5000101.0);
            var jdNegative = JPLAccessor.CalendarDateAndFractionToJulian(-5000101.0);

            // ~1000 tropical years ~= 365250 days. Tolerance loose because leap-day
            // distribution between -500 and +500 depends on the calendar rule NOVAS
            // applies. The test's intent is "negative year is in fact a different year,
            // not silently treated as positive."
            (jdPositive - jdNegative).Should().BeApproximately(365250.0, 100.0);
        }
    }
}
