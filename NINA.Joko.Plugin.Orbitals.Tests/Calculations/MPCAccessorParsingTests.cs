using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    [TestFixture]
    public class MPCAccessorParsingTests {

        // MPC fixed-column widths used by MPCCometResponse parser:
        //   periodicCometNumber (4) orbitType (1) provDesignation (9) year (5) month (3)
        //   dayTT (8) q_au (11) e (10) argPerihelion_deg (10) node_deg (10) inclination_deg (10)
        //   epoch yyyyMMdd (8) [skip 2] absMag (5) slope (4) name (59) reference (rest)
        //
        // REF: MPC distribution format for periodic comets (CometEls.txt).
        // The synthetic line below is constructed with exact column widths so the
        // manual offset-driven parser walks it cleanly. Values are loosely modeled
        // on the 1P/Halley 1986 entry but use round numbers for legibility.

        // Build the fixture in code so widths are explicit (and easy to verify).
        private static readonly string HalleyLine = string.Concat(
            "   1".PadLeft(4),                          // cometNumber  width 4
            "P",                                          // orbitType    width 1
            "         ".PadRight(9),                     // provDesig    width 9 (none for 1P)
            " 1986".PadLeft(5),                          // year         width 5
            "  2".PadLeft(3),                            // month        width 3
            "  9.4585".PadLeft(8),                       // day          width 8
            "    0.58610".PadLeft(11),                   // q            width 11
            " 0.9671429".PadLeft(10),                    // e            width 10
            " 111.3325".PadLeft(10),                     // w            width 10
            "  58.4201".PadLeft(10),                     // node         width 10
            " 162.2627".PadLeft(10),                     // i            width 10
            "19860320",                                   // epoch        width 8
            "  ",                                         // 2-char skip
            "  5.5",                                      // absMag       width 5
            " 4.0",                                       // slope        width 4
            "1P/Halley".PadRight(59),                    // name         width 59
            "JPL 73"                                      // reference    trailing
        );

        private static StreamReader FromString(string content) =>
            new StreamReader(new MemoryStream(Encoding.ASCII.GetBytes(content)));

        [Test]
        public void CometResponse_ParsesHalleyRowIntoExpectedFields() {
            using var response = new MPCCometResponse(FromString(HalleyLine + "\n"));

            var rows = response.Response.ToList();

            rows.Should().HaveCount(1);
            var halley = rows[0];
            halley.number.Should().Be(1);
            halley.designation.Should().Be(MPCCometDesignation.LongPeriod, "orbitType 'P' maps to LongPeriod");
            halley.tpYear.Should().Be(1986);
            halley.tpMonth.Should().Be(2);
            halley.tpDay_tt.Should().BeApproximately(9.4585, 1e-6);
            halley.perihelionDistance_au.Should().BeApproximately(0.58610, 1e-6);
            halley.eccentricity.Should().BeApproximately(0.9671429, 1e-7);
            halley.argOfPerihelion_deg.Should().BeApproximately(111.3325, 1e-4);
            halley.longOfAscendingNode_deg.Should().BeApproximately(58.4201, 1e-4);
            halley.incAscendingNode_deg.Should().BeApproximately(162.2627, 1e-4);
            halley.epoch.Should().Be(new DateTime(1986, 3, 20));
            halley.absoluteMagnitude.Should().BeApproximately(5.5, 1e-6);
            halley.slope.Should().BeApproximately(4.0, 1e-6);
            halley.name.Trim().Should().Be("1P/Halley");
            halley.reference.Trim().Should().Be("JPL 73");
        }

        [Test]
        public void CometResponse_EmptyInput_YieldsNoRows() {
            using var response = new MPCCometResponse(FromString(string.Empty));

            response.Response.Should().BeEmpty();
        }

        [Test]
        public void CometResponse_BlankLine_TerminatesParsing() {
            // A blank/whitespace-only line is the documented end-of-input signal.
            using var response = new MPCCometResponse(FromString(HalleyLine + "\n\n" + HalleyLine + "\n"));

            response.Response.Should().HaveCount(1, "the blank line stops parsing before the second record");
        }

        [TestCase("P", MPCCometDesignation.LongPeriod)]
        [TestCase("C", MPCCometDesignation.ShortPeriod)]
        [TestCase("D", MPCCometDesignation.Defunct)]
        [TestCase("A", MPCCometDesignation.MinorPlanet)]
        [TestCase("X", MPCCometDesignation.UncertainObject)]
        [TestCase("I", MPCCometDesignation.InterstellarObject)]
        public void CometResponse_OrbitTypeMapping(string code, MPCCometDesignation expected) {
            // The implementation defines this mapping privately; parse a synthetic
            // single-line file with the given orbit-type character and verify the
            // public Response surfaces it as the expected designation enum value.
            var line = string.Concat(
                "    ",                              // empty cometNumber
                code,                                // orbitType (single char)
                "         ".PadRight(9),
                " 2024".PadLeft(5),
                "  1".PadLeft(3),
                "  1.0000".PadLeft(8),
                "    1.00000".PadLeft(11),
                " 0.5000000".PadLeft(10),
                "  10.0000".PadLeft(10),
                "  20.0000".PadLeft(10),
                "  30.0000".PadLeft(10),
                "20240101",
                "  ",
                "  5.5",
                " 4.0",
                "synthetic".PadRight(59),
                "TEST"
            );

            using var response = new MPCCometResponse(FromString(line + "\n"));
            response.Response.Single().designation.Should().Be(expected);
        }
    }
}
