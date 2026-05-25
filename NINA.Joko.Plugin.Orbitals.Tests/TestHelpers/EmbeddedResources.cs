using System;
using System.IO;
using System.Reflection;

namespace NINA.Joko.Plugin.Orbitals.Tests.TestHelpers {

    internal static class EmbeddedResources {
        private static readonly Assembly Assembly = typeof(EmbeddedResources).Assembly;
        private const string Prefix = "NINA.Joko.Plugin.Orbitals.Tests.TestHelpers.ReferenceData.";

        public static Stream Open(string fileName) {
            var resourceName = Prefix + fileName;
            var stream = Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) {
                throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' not found. " +
                    "Confirm the file lives under TestHelpers/ReferenceData/ and that the csproj " +
                    "<EmbeddedResource> glob matches it.");
            }
            return stream;
        }

        public static string ReadAllText(string fileName) {
            using var stream = Open(fileName);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
