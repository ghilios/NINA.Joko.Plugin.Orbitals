using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Tests.TestHelpers;
using NUnit.Framework;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace NINA.Joko.Plugin.Orbitals.Tests.Calculations {

    /// <summary>
    /// Covers importing element files the user supplied instead of downloading, which is the
    /// escape hatch for observatory PCs whose IPs the MPC servers block.
    ///
    /// Format is detected from content rather than extension, so these tests feed the real
    /// published layouts (and the HTML error page people accidentally save instead).
    /// </summary>
    [TestFixture]
    public class OrbitalElementsFileImporterTests {

        private string tempDir;

        [SetUp]
        public void Setup() {
            tempDir = Path.Combine(Path.GetTempPath(), "OrbitalsImportTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void Teardown() {
            try {
                if (Directory.Exists(tempDir)) {
                    Directory.Delete(tempDir, recursive: true);
                }
            } catch { /* best effort */ }
        }

        private string WriteFile(string name, string content) {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private string WriteGzippedFile(string name, string content) {
            var path = Path.Combine(tempDir, name);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var gs = new GZipStream(fs, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gs, Encoding.UTF8)) {
                writer.Write(content);
            }
            return path;
        }

        [Test]
        public void Import_MpcCometFile_DetectedAsMpc() {
            var path = WriteFile("CometEls.txt", EmbeddedResources.ReadAllText("CometEls.sample.txt"));

            var result = OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            result.Source.Should().Be(OrbitalElementsSourceEnum.MPC);
            result.Count.Should().BeGreaterThan(0);
            result.Elements.Should().OnlyContain(e => e.Source == OrbitalElementsSourceEnum.MPC);
            result.Elements.Select(e => e.Name).Should().Contain(n => n.StartsWith("220P"));
        }

        [Test]
        public void Import_JplCometFile_DetectedAsJpl() {
            var path = WriteFile("ELEMENTS.COMET", EmbeddedResources.ReadAllText("ELEMENTS.COMET.sample.txt"));

            var result = OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            result.Source.Should().Be(OrbitalElementsSourceEnum.JPL);
            result.Count.Should().BeGreaterThan(0);
            result.Elements.Should().OnlyContain(e => e.Source == OrbitalElementsSourceEnum.JPL);
        }

        /// <summary>Detection is by content, so a misleading file name must not change the outcome.</summary>
        [Test]
        public void Import_IgnoresFileNameAndExtension() {
            var path = WriteFile("definitely-jpl.dat", EmbeddedResources.ReadAllText("CometEls.sample.txt"));

            var result = OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            result.Source.Should().Be(OrbitalElementsSourceEnum.MPC);
        }

        /// <summary>The JPL asteroid bundles ship gzipped and users often keep them that way.</summary>
        [Test]
        public void Import_GzippedFile_IsDecompressedTransparently() {
            var path = WriteGzippedFile("ELEMENTS.COMET.gz", EmbeddedResources.ReadAllText("ELEMENTS.COMET.sample.txt"));

            var result = OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            result.Source.Should().Be(OrbitalElementsSourceEnum.JPL);
            result.Count.Should().BeGreaterThan(0);
        }

        /// <summary>
        /// The most common real failure: the user saved the site's HTML error page. The
        /// message has to echo the first line, or they have no way to tell what went wrong.
        /// </summary>
        [Test]
        public void Import_HtmlErrorPage_FailsWithTheFirstLineQuoted() {
            var path = WriteFile("CometEls.txt", EmbeddedResources.ReadAllText("BlockedPage.sample.html"));

            var act = () => OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            act.Should().Throw<OrbitalElementsImportException>()
               .Where(e => e.Message.Contains("First line was")
                        && e.Message.Contains("DOCTYPE")
                        && e.Message.Contains("CometEls.txt"));
        }

        [Test]
        public void Import_EmptyFile_Fails() {
            var path = WriteFile("empty.txt", "   \n  \n");

            var act = () => OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.Comet);

            act.Should().Throw<OrbitalElementsImportException>()
               .WithMessage("*empty*");
        }

        [Test]
        public void Import_MissingFile_Fails() {
            var act = () => OrbitalElementsFileImporter.Import(
                Path.Combine(tempDir, "nope.txt"), OrbitalObjectTypeEnum.Comet);

            act.Should().Throw<OrbitalElementsImportException>()
               .WithMessage("*not found*");
        }

        /// <summary>An MPC comet file offered as an asteroid import must not be silently accepted.</summary>
        [Test]
        public void Import_MpcCometFileAsAsteroids_Fails() {
            var path = WriteFile("CometEls.txt", EmbeddedResources.ReadAllText("CometEls.sample.txt"));

            var act = () => OrbitalElementsFileImporter.Import(path, OrbitalObjectTypeEnum.NumberedAsteroids);

            act.Should().Throw<OrbitalElementsImportException>();
        }
    }
}
