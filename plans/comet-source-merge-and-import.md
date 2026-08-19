# Comet elements: "Both" merged source, per-object provenance, and offline file import

## Context

The Orbitals plugin downloads comet orbital elements from one of two rival datasets, chosen
by a dropdown in the main pane (`IOrbitalsOptions.CometAccessor`). I measured both live files
on 2026-08-19:

- **JPL** (`ssd.jpl.nasa.gov/dat/ELEMENTS.COMET`) — 4072 comets. Broad coverage.
- **MPC** (`minorplanetcenter.net/iau/MPCORB/CometEls.txt`) — 954 comets. Far fresher.

Of **920 comets present in both and unambiguously matchable, MPC had the newer epoch in
100% of cases** — median 8.5 years newer, max 84 years. JPL carries **2919 comets MPC
lacks**; MPC has 29 JPL lacks. Only **1.4%** of JPL's entries have an epoch under a year
old; 78.6% are more than a decade stale, because JPL publishes each comet's osculating
elements at that orbit solution's own reference epoch rather than a common current one.

This is not cosmetic. For 220P/McNaught the JPL entry puts the comet **84.5 arcminutes**
off its true position (vs 0.41 arcsec from MPC) — the user images empty sky. So today's
dropdown is a forced choice between coverage and correctness, with nothing in the UI
explaining the tradeoff.

Separately, users running observatory PCs behind VPN gateways report **their IPs being
blocked by MPC's servers**, so the download simply fails and they have no recourse.

This change adds a merged "Both" source, makes each object's provenance persisted and
visible, and lets users hand the plugin an element file they fetched elsewhere.

UI/UX was designed by Fable; the layout, copy, and interaction decisions below are its
design, with two corrections noted inline.

## Decisions taken

- **"Both" becomes the default**, and existing profiles are migrated to it once (both JPL
  and MPC users). Migrating MPC users is safe: the merge only ever *adds* objects and
  prefers the newer epoch, and MPC won every overlap, so nothing they had gets worse.
- **File import covers all three element types** (comets, numbered, un-numbered asteroids).
- **An imported file stands in for that source's download** rather than becoming a fourth
  source. A blocked user imports `CometEls.txt` once and "Both" still works — JPL from the
  network, MPC from their file.

---

## Architecture: per-feed stores, merge derived at load

The pivotal structural decision. Each *feed* keeps its own persisted store, exactly as
today (`CometElements.bin.gz` = JPL, `MPC_CometElements.bin.gz` = MPC — both already
coexist on disk). "Both" is a **view computed when building the backend**, not a third
file.

This is what makes the rest fall out cheaply:

- **Partial failure**: if MPC is unreachable, its store is simply older. Last week's MPC
  elements are still ~8 years fresher than today's JPL, so the merge must keep using them.
  A failed download degrades the *age* of a feed, never its *presence*.
- **Import**: writes into one feed's store and is then indistinguishable from a download.
- **Switching the dropdown**: already triggers a reload from disk
  (`OrbitalElementsAccessor.Options_PropertyChanged`), so it just re-derives the merge.

Consequence: `IOrbitalElementsAccessor.Update` currently derives its save path from the
*current option value* via `GetObjectTypeSavePath` → `GetAccessor`. It must instead take
the feed explicitly. Add an overload
`Update(OrbitalObjectTypeEnum objectType, OrbitalElementsSourceEnum source, IEnumerable<IOrbitalElementsSource>, IProgress<ApplicationStatus>, CancellationToken)`.

---

## 1. Data model

`Enums/OrbitalElementsAccessorEnum.cs` — add the third policy value:

```csharp
[Description("Both (JPL + MPC)")]
JPLAndMPC = 2
```

Leave `JPL`/`MPC` labels bare — "Both (JPL + MPC)" already implies the others are
single-source, and `[Description]` renders in the *closed* combo, so no "(recommended)"
suffix there. The recommendation goes in the tooltip.

New `Enums/OrbitalElementsSourceEnum.cs` — per-object provenance, distinct from the policy
enum: `Unknown = 0, JPL = 1, MPC = 2`.

**Note there is no `File` value.** Per the chosen import model, an imported file *is* the
JPL or MPC feed's data; how it was acquired is feed-level state, not object-level. (Fable's
section 2 lists `File` as a third provenance value, contradicting its own section 0 — this
resolves it in favour of section 0.)

`Calculations/Kepler.cs` — `OrbitalElements` gets:

```csharp
[ProtoMember(13)] public OrbitalElementsSourceEnum Source { get; set; }
```

Tag 13 is the next free one. Backward compatible: existing `.bin.gz` caches simply lack it
and deserialize as `Unknown`. **Backfill on load** — `LoadObjectType` knows which feed file
it just read, so stamp `Source` on any record that comes back `Unknown`. Existing users
therefore never see "Unknown" without re-downloading.

`Interfaces/IOrbitalElementsSource.cs` — add `OrbitalElementsSourceEnum Source { get; }`.
`JPLOrbitalElements` returns `JPL`, `MPCCometElements` returns `MPC`, and each
`ToOrbitalElements()` copies it through.

Per-feed acquisition metadata (how/when/from-what-file) does not fit the existing
length-prefixed protobuf stream. Write a small **sidecar JSON** next to each store
(`MPC_CometElements.meta.json`): `{ source, acquiredVia: Download|Import, acquiredAt,
sourceFileName, recordCount }`. Absent sidecar = downloaded, timestamp falls back to
`File.GetLastWriteTime` as today.

---

## 2. The merge

New `Calculations/CometElementsMerger.cs`. Takes the two `IEnumerable<IOrbitalElementsSource>`
and returns one merged sequence for `Update`.

**Deduplication is a correctness requirement, not an optimisation.**
`TrigramStringMap.Lookup` throws `DuplicateKeyException` when two rows share a `Name`, and
`OrbitalElementsAccessor.Get` turns that into "Multiple results found" + `null` — so a
naive concatenation would make every overlapping comet *unloadable*.

Names do not match across the files (`10P/Tempel` vs `10P/Tempel 2`), so match on a
**normalized designation key**, which I measured at **97.0%** against the live files:

- Numbered periodic: `{number}{type}[-{fragment}]` — e.g. `220P`, `141P-A`. The fragment
  appears in different places in each file (MPC `141P-A/Machholz`, JPL
  `73P/Schwassmann-Wachmann 3-A`), so check both positions.
- Interstellar: `{number}I` (`1I`, `2I`, `3I`).
- Otherwise the provisional designation: `C/2021 Y1`, with an optional `-{fragment}`.
- Fall back to the trimmed name.

**Be safe on ambiguity**: if a key maps to more than one row *within* a single source, do
not merge that key — keep those rows separate. Cross-assigning a fragment's elements to its
parent comet would be worse than staleness. (Measured: 20 ambiguous JPL keys, 5 MPC, all
fragment families.)

Winner is the larger `Epoch_jd`; ties go to MPC. The winner's own `Name` is retained so
search keeps finding the familiar string. Residual unmatched entries (28 MPC-only, ~2900
JPL-only) pass through unchanged — that is the coverage win, not a failure.

### Name uniqueness: suffix the source when ambiguous

**Any `Name` that would appear more than once in the merged output gets its source appended
— `10P/Tempel (MPC)` and `10P/Tempel (JPL)`** — so the user can see at a glance which is
which, and so no two records ever share a name.

This is both a usability rule and a hard correctness requirement:
`TrigramStringMap.Lookup` throws `DuplicateKeyException` on a duplicate name and
`OrbitalElementsAccessor.Get` turns that into "Multiple results found" + `null`, which would
make the affected comets unloadable.

Measured against today's live files this fires on **0 rows** — 838 name strings appear in
both files, but every one of them has an unambiguous key and merges to a single entry. So
this is a safety net for future file changes, not routine clutter; normal use will never
show a suffix. It must still be implemented and tested, because the failure it prevents is
silent and total for the affected object.

Trigram search is unaffected (searching `10P/Tempel` still matches the suffixed name), but
**exact lookup is**: a saved sequence target referencing a bare name would stop resolving if
that name later gains a suffix. `Get` should therefore fall back to trying
`"{name} (MPC)"` then `"{name} (JPL)"` when the exact match misses.

Return the counts (`fromMpc`, `fromJpl`, `total`) alongside the merged sequence for the
transparency reporting in §5.

---

## 3. Update flow

`ViewModels/OrbitalsVM.UpdateCometElements` already branches on `CometAccessor`; extend it:

- `JPLAndMPC` → fetch both feeds concurrently, write **each to its own store**, then
  rebuild the comet backend from the merge.
- The existing "already up to date" short-circuit compares one remote `Last-Modified`
  against the local file mtime. Under "Both" compare each feed independently and skip only
  the feeds that are current.
- **Partial failure is expected, not exceptional.** If one feed throws, keep its stored
  data, complete the merge with the other, and surface the state (§6). Only when *both*
  fail is it an error, and then leave all stores untouched.

---

## 4. UI (Fable's design)

### Source dropdown
Existing `PART_CometAccessorEnumList` in `View/OrbitalsView.xaml` picks up the third value
automatically via `EnumBindingSource`. Add a tooltip resource — three parallel lines, one
judgment each:

```
JPL  — ~4,000 comets, but elements are often years out of date
MPC  — ~950 comets, refreshed within days of new observations
Both — downloads both, keeps the newer elements for each comet (recommended)
```

### Migration
Default `JPLAndMPC` for new profiles; migrate existing profiles once behind a persisted
`CometAccessorDefaultMigrated` flag, with a one-time notification explaining the change and
how to revert.

### Provenance — three surfaces
1. **Pill beside the object name** (main pane §3 header). Quiet, theme-brushed, no colour:
   `C/2023 A3 (Tsuchinshan-ATLAS)  [MPC · epoch 2026-07-02]`. Pairing source *with epoch*
   is deliberate — together they answer "can I trust this pointing tonight".
2. **`Source` row in the properties grid** (main pane §4) — the long form:
   `MPC (downloaded 2026-08-19)` / `MPC — imported from CometEls.txt (2026-08-12)`.
3. **Search autocomplete `Column3`** — `IAutoCompleteItem` already has three columns and
   only `Column1` is populated (`OrbitalSearchVM`). Put the bare tag (`MPC`/`JPL`) there so
   the user can see which entry they're about to load. Leave `Column2` empty.

**Not the sequencer.** The `Name | RA | Dec | PA°` header stays untouched across all four
near-duplicate templates — provenance is diagnostic, and the sequencer header is for
pointing verification.

### Import
An `Import…` button in each element row, immediately beside `Update` — the stranded user's
path is *click Update → it fails → the escape hatch is the next button over*. Tooltip names
the exact URLs to fetch on another machine.

Reuse the existing programmatic `Microsoft.Win32.OpenFileDialog` +
`Dispatcher.InvokeAsync` precedent from `Imaging/XisfStubCaptureSource.cs`; there is no
declarative file-picker idiom in this codebase and no new window is warranted.

**Format is auto-detected from content, never extension** — a user troubleshooting at 2am
should not be asked what format their file is. Sniff gzip magic (`1F 8B`) first and
decompress (asteroid feeds are `.gz`), then distinguish JPL's header+dashes layout from
MPC's fixed-column records. The parsers are already `StreamReader`-based
(`JPLCometResponse`, `MPCCometResponse`, …), so this needs only a `GetFromFile` entry
point, no changes to the parsing logic.

> Correction to Fable's §4: it assumed asteroid import is an MPC workaround. The plugin's
> asteroid feeds are **JPL-only** (`ELEMENTS.NUMBR.gz` / `ELEMENTS.UNNUM.gz`), so asteroid
> import accepts JPL-format gzipped files.

Failure message echoes the first line of the file — this catches the classic case of having
saved an HTML error page instead of the raw data.

---

## 5. Merge transparency

- Completion toast: *"Comet elements updated: 3,921 total — 920 from MPC (newer epochs),
  3,001 from JPL."*
- Persistent tooltip on the `Comets (3921)` count with the same breakdown plus each feed's
  acquisition time and whether it came from a file.

No permanent status line — a merge report is a weekly curiosity, and dead pixels the rest
of the time.

---

## 6. Failure states

- **MPC fails, JPL succeeds** (the important one): merge with MPC's *stored* data, warning
  toast naming the workaround, and a persistent inline badge under the comet row —
  *"MPC download failed on last update — using MPC data from 2026-08-12"* — cleared by the
  next successful update or import. The badge is what tells the 2am troubleshooter that
  Import is their path if they missed the toast. Use the JWST badge idiom
  (`SequenceItems/DataTemplates.xaml`) with a **warning**, never error, brush.
- **MPC fails with no stored MPC data**: same badge, *"comets are from JPL only (elements
  may be years stale). Use Import if MPC blocks your network."*
- **Both fail**: error toast naming both causes; stores and `Last Updated` untouched.
- **Empty state**: when the relevant count is zero, set the autocomplete `HintText` to
  *"No comet elements loaded — click Update above"*. No new UI, just the right words in an
  existing slot.

---

## Deliberately out of scope

Fable also designed a **staleness warning** (badge when the selected object's epoch is >5
years old, with wording that differs depending on whether a fresher source is available).
It is a good idea and directly addresses the failure mode that started this work, but it
was not requested — leaving it out keeps this change reviewable. Worth a follow-up.

---

## Files

| Area | Files |
|---|---|
| Enums | `Enums/OrbitalElementsAccessorEnum.cs`, new `Enums/OrbitalElementsSourceEnum.cs` |
| Model | `Calculations/Kepler.cs` (`OrbitalElements`, tag 13), `Interfaces/IOrbitalElementsSource.cs` |
| Merge | new `Calculations/CometElementsMerger.cs` |
| Persistence | `Calculations/OrbitalElementsAccessor.cs` (per-feed `Update`, merged load, sidecar metadata, import) |
| Parsers | `Calculations/JPLAccessor.cs`, `Calculations/MPCAccessor.cs` (`GetFromFile` + format sniffing) |
| Options | `OrbitalsOptions.cs`, `Interfaces/IOrbitalsOptions.cs` (default + migration flag) |
| VM | `ViewModels/OrbitalsVM.cs` (merge update, import commands, counts/tooltips), `ViewModels/OrbitalSearchVM.cs` (`Column3`) |
| View | `View/OrbitalsView.xaml` (tooltip, Import buttons, pill, Source row, failure badge) |

---

## Verification

1. `dotnet build` the plugin **and** `dotnet test` from the repo root — baseline is
   **304/304 passing** after PR #21.
2. **Merger unit tests** are the core of this change. Use the existing
   `SyntheticOrbitalSource : IOrbitalElementsSource` pattern from
   `OrbitalElementsAccessorTests.cs`:
   - designation-key normalization across both files' fragment conventions
     (`141P-A/Machholz` ↔ `73P/Schwassmann-Wachmann 3-A` shapes);
   - newer epoch wins, ties go to MPC;
   - ambiguous keys are *not* merged;
   - JPL-only and MPC-only entries survive;
   - **no duplicate `Name` in the output** — assert directly over a full merge of the real
     checked-in sample files, since this is what `TrigramStringMap.Lookup` would throw on;
   - **colliding names get `(JPL)`/`(MPC)` appended** — construct the collision
     deliberately (it does not occur in today's data), assert both survive with distinct
     suffixed names, and assert `Get` still resolves the bare name via the fallback.
3. **Round-trip test** that a merged dataset can be `Update`d then `Get`/`Search`ed for a
   comet present in both sources (220P/McNaught) without `DuplicateKeyException`.
4. **Provenance persistence**: `Update` → reload → `Source` survives; and a record written
   before tag 13 existed (hand-built protobuf without the field) backfills correctly on load.
5. **Import**: parse a real `CometEls.txt` and a real `ELEMENTS.COMET` from disk via the
   sniffing path; assert format detection, gzip handling, and that an HTML error page is
   rejected with the first line echoed. `TestHelpers/EmbeddedResources.cs` and the
   `TestHelpers/ReferenceData/**` csproj glob already exist for exactly this and are
   currently unused — check in trimmed sample files there.
6. **Accuracy regression**: extend `CometEphemerisTests` so 220P/McNaught resolved through
   a *merged* dataset matches the MPC-sourced reference (RA 45.305221, Dec +9.484476,
   30″ tolerance) rather than the 84.5′-off JPL entry. This is the end-to-end proof the
   feature does what it claims.
7. Manual: in NINA, switch to Both and Update; confirm the count jumps to ~3.9k, the
   breakdown toast/tooltip is right, 220P shows an `MPC` pill, and a JPL-only comet shows
   `JPL`. Then block MPC (hosts-file entry) and re-Update to confirm the partial-failure
   badge and that MPC data is retained. Then Import a downloaded `CometEls.txt` and confirm
   it clears the badge and persists across a restart.
8. Copy this plan to `plans/` and commit it with the implementation, per `CLAUDE.md`.
