#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.Enums;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static NINA.Joko.Plugin.Orbitals.Calculations.Kepler;

namespace NINA.Joko.Plugin.Orbitals.Calculations {

    /// <summary>
    /// Combines the JPL and MPC comet datasets into one, keeping whichever entry for a
    /// given comet has the more recent epoch.
    ///
    /// The two feeds are complementary rather than redundant. Measured against the live
    /// files on 2026-08-19: of 920 comets present in both and unambiguously matchable, MPC
    /// had the newer epoch in 100% of cases (median 8.5 years newer), because JPL publishes
    /// each comet's osculating elements at that orbit solution's own reference epoch rather
    /// than a common current one. But JPL carries ~2900 comets MPC does not list at all. So
    /// merging gives MPC's accuracy where available and JPL's coverage elsewhere.
    ///
    /// Staleness here is not cosmetic: propagating JPL's 2020-epoch elements for
    /// 220P/McNaught to 2026 puts it 84.5 arcminutes off its true position.
    /// </summary>
    public static class CometElementsMerger {

        public class MergeResult {

            public MergeResult(IReadOnlyList<OrbitalElements> elements, int fromJpl, int fromMpc, int disambiguated) {
                this.Elements = elements;
                this.FromJPL = fromJpl;
                this.FromMPC = fromMpc;
                this.Disambiguated = disambiguated;
            }

            public IReadOnlyList<OrbitalElements> Elements { get; }
            public int FromJPL { get; }
            public int FromMPC { get; }
            public int Total => Elements.Count;

            /// <summary>How many entries had their source appended to disambiguate a duplicate name.</summary>
            public int Disambiguated { get; }

            public override string ToString() {
                return $"{{{nameof(Total)}={Total}, {nameof(FromJPL)}={FromJPL}, {nameof(FromMPC)}={FromMPC}, {nameof(Disambiguated)}={Disambiguated}}}";
            }
        }

        // Fragment suffix, e.g. the "-A" in 141P-A/Machholz or 73P/Schwassmann-Wachmann 3-A.
        private const string FragmentPattern = @"(?:-([A-Z]{1,2}))";

        private static readonly Regex NumberedPeriodicRegex =
            new Regex(@"^(\d+)([PDCXAI])" + FragmentPattern + @"?/(.*)$", RegexOptions.Compiled);

        private static readonly Regex InterstellarRegex =
            new Regex(@"^(\d+)(I)/", RegexOptions.Compiled);

        private static readonly Regex ProvisionalRegex =
            new Regex(@"^([PDCXAI])" + FragmentPattern + @"?/\s*(\d{4})\s+([A-Z]{1,2}\d*)" + FragmentPattern + @"?", RegexOptions.Compiled);

        private static readonly Regex TrailingFragmentRegex =
            new Regex(FragmentPattern + @"$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// Reduces a comet name to a designation key that is stable across the two feeds.
        ///
        /// The feeds do not agree on names -- MPC writes "10P/Tempel" where JPL writes
        /// "10P/Tempel 2" -- so matching on the name alone loses about 12% of the overlap.
        /// They also place fragment letters differently: MPC puts them before the slash
        /// ("141P-A/Machholz") and JPL at the end of the name
        /// ("73P/Schwassmann-Wachmann 3-A"), so both positions are checked.
        ///
        /// Measured 97.0% key agreement across the live files; the residual is objects
        /// genuinely listed by only one of the two.
        /// </summary>
        public static string GetDesignationKey(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return string.Empty;
            }

            var normalized = WhitespaceRegex.Replace(name.Trim(), " ");

            var numbered = NumberedPeriodicRegex.Match(normalized);
            if (numbered.Success) {
                var number = int.Parse(numbered.Groups[1].Value);
                var type = numbered.Groups[2].Value;
                var fragment = numbered.Groups[3].Success ? numbered.Groups[3].Value : null;
                if (fragment == null) {
                    var trailing = TrailingFragmentRegex.Match(numbered.Groups[4].Value);
                    if (trailing.Success) {
                        fragment = trailing.Groups[1].Value;
                    }
                }
                return $"{number}{type}" + (fragment != null ? $"-{fragment}" : string.Empty);
            }

            var interstellar = InterstellarRegex.Match(normalized);
            if (interstellar.Success) {
                return $"{int.Parse(interstellar.Groups[1].Value)}I";
            }

            var provisional = ProvisionalRegex.Match(normalized);
            if (provisional.Success) {
                var type = provisional.Groups[1].Value;
                var fragment = provisional.Groups[2].Success ? provisional.Groups[2].Value
                    : provisional.Groups[5].Success ? provisional.Groups[5].Value : null;
                var year = provisional.Groups[3].Value;
                var code = provisional.Groups[4].Value;
                return $"{type}/{year} {code}" + (fragment != null ? $"-{fragment}" : string.Empty);
            }

            return normalized;
        }

        public static MergeResult Merge(
            IEnumerable<IOrbitalElementsSource> jplElements,
            IEnumerable<IOrbitalElementsSource> mpcElements) {
            var jpl = Materialize(jplElements, OrbitalElementsSourceEnum.JPL);
            var mpc = Materialize(mpcElements, OrbitalElementsSourceEnum.MPC);

            // A key that maps to more than one row inside a single feed cannot be matched
            // safely -- these are fragment families (measured: 20 such keys in JPL, 5 in
            // MPC). Assigning a fragment's elements to its parent comet would be a worse
            // failure than staleness, so those rows are passed through unmerged.
            var jplByKey = GroupUnambiguous(jpl);
            var mpcByKey = GroupUnambiguous(mpc);

            var merged = new List<OrbitalElements>(jpl.Count + mpc.Count);
            var consumedJpl = new HashSet<OrbitalElements>();
            var fromJpl = 0;
            var fromMpc = 0;

            foreach (var candidate in mpc) {
                var key = GetDesignationKey(candidate.Name);
                if (jplByKey.TryGetValue(key, out var jplMatch) && mpcByKey.ContainsKey(key)) {
                    consumedJpl.Add(jplMatch);
                    // Ties go to MPC: it republishes every comet at a single current epoch,
                    // so it is the better bet when the two agree on age.
                    if (EpochOf(candidate) >= EpochOf(jplMatch)) {
                        merged.Add(candidate);
                        fromMpc++;
                    } else {
                        merged.Add(jplMatch);
                        fromJpl++;
                    }
                } else {
                    merged.Add(candidate);
                    fromMpc++;
                }
            }

            foreach (var candidate in jpl) {
                if (consumedJpl.Contains(candidate)) {
                    continue;
                }
                merged.Add(candidate);
                fromJpl++;
            }

            var disambiguated = DisambiguateNames(merged);
            return new MergeResult(merged, fromJpl, fromMpc, disambiguated);
        }

        /// <summary>
        /// Appends the source to any name that would otherwise appear more than once, e.g.
        /// "10P/Tempel (MPC)" and "10P/Tempel (JPL)".
        ///
        /// This is a correctness requirement as much as a usability one: TrigramStringMap
        /// throws DuplicateKeyException when two records share a name, and
        /// OrbitalElementsAccessor.Get turns that into a null result, which would make the
        /// affected comets silently unloadable.
        ///
        /// Against the live files this fires on zero rows -- 838 name strings appear in both
        /// feeds, but every one of them has an unambiguous key and merges to a single entry.
        /// It exists for the cases the published files have not thrown at us yet.
        /// </summary>
        private static int DisambiguateNames(List<OrbitalElements> elements) {
            var byName = new Dictionary<string, List<OrbitalElements>>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in elements) {
                var name = element.Name ?? string.Empty;
                if (!byName.TryGetValue(name, out var bucket)) {
                    bucket = new List<OrbitalElements>();
                    byName[name] = bucket;
                }
                bucket.Add(element);
            }

            var disambiguated = 0;
            foreach (var bucket in byName.Values) {
                if (bucket.Count < 2) {
                    continue;
                }
                foreach (var element in bucket) {
                    element.Name = $"{element.Name} ({DescribeSource(element.Source)})";
                    disambiguated++;
                }
            }
            return disambiguated;
        }

        private static string DescribeSource(OrbitalElementsSourceEnum source) {
            return source == OrbitalElementsSourceEnum.MPC ? "MPC"
                 : source == OrbitalElementsSourceEnum.JPL ? "JPL"
                 : "Unknown";
        }

        private static List<OrbitalElements> Materialize(
            IEnumerable<IOrbitalElementsSource> source, OrbitalElementsSourceEnum sourceEnum) {
            var result = new List<OrbitalElements>();
            if (source == null) {
                return result;
            }
            foreach (var row in source) {
                var elements = row.ToOrbitalElements();
                if (elements.Source == OrbitalElementsSourceEnum.Unknown) {
                    elements.Source = sourceEnum;
                }
                result.Add(elements);
            }
            return result;
        }

        private static Dictionary<string, OrbitalElements> GroupUnambiguous(List<OrbitalElements> elements) {
            var counts = new Dictionary<string, int>();
            var first = new Dictionary<string, OrbitalElements>();
            foreach (var element in elements) {
                var key = GetDesignationKey(element.Name);
                if (string.IsNullOrEmpty(key)) {
                    continue;
                }
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
                if (count == 0) {
                    first[key] = element;
                }
            }
            foreach (var entry in counts) {
                if (entry.Value > 1) {
                    first.Remove(entry.Key);
                }
            }
            return first;
        }

        private static double EpochOf(OrbitalElements elements) {
            return double.IsNaN(elements.Epoch_jd) ? double.NegativeInfinity : elements.Epoch_jd;
        }
    }
}
