# Disc Scan Rollout (Replacing MakeMKV Logs)

## Status (source reviewed 2026-10-29)

🟡 **Implementation delivered through phase 2; rollout/publication incomplete.**
The table below distinguishes built behavior from the checked-in defaults.
`appsettings.json` has `DiscScanMode = Optional` and `ShowScanFileUpload = false`;
production overrides and dashboard thresholds have not been verified by this
source review. Built phase 2 is not evidence that the rollout gate was met.

## Overview

The browser disc scan produces an Optical Disc Manifest (ODM, `.odm.json`) that reproduces the
title list MakeMKV reports for DVDs, Blu-rays and UHDs. This plan rolls the scan out in three
phases so that MakeMKV logs can eventually be retired.

| Phase | State | Description |
|---|---|---|
| 1. Collect both | 🟢 Built | The MakeMKV log is required and drives identification. After the log uploads, an interstitial page (`/contribution/{id}/discs/{discId}/scan`) offers a browser scan, or lets contributors skip to identify. A scan-file upload (from the CLI) is also offered there when `Contributions:ShowScanFileUpload` is `true` (off by default). The server stores both and compares them. |
| 2. Scan by default | 🟡 Built; off by source default | Set `Contributions:DiscScanMode` to `Default` after the parity gate below. The scan becomes the first upload option and the identification source, and MakeMKV logs move to "Advanced". |
| 3. Scan first, no logs | 🔵 Proposed | The scan becomes the first contribution step, replacing the TMDB lookup, and MakeMKV logs are no longer accepted. |

## How it works

- **Storage.** A contribution disc can hold a log blob (`{c}/{d}-logs.txt`), a manifest blob
  (`ContributionDiscAssets.ManifestPath`), or both. `UserContributionDisc.ManifestUploaded` and
  `ManifestUserAgent` record the scan. A failed scan never changes the log state.
- **Comparison.** `DiscLogManifestComparer` (in `TheDiscDb.Contributions`) runs after either upload
  when both blobs exist, through `IDiscScanComparisonService`. Every run is appended (newest first,
  last 20 kept) to a readable JSON file next to the log and scan, `{c}/{d}-comparison.json`
  (`ContributionDiscAssets.ComparisonPath`, via `DiscScanComparisonStore`). Comparisons are
  rollout-only data, so they are kept out of the database and are not published.
  - Disc identity is checked first using the content hash; a scan of another disc is reported as
    `DifferentDisc`.
  - Titles are matched by source key: the playlist or stream file on Blu-ray, the title number on
    DVD.
  - Chapters, size, length and segment map are hard differences. Display size is a soft difference.
  - Technical parity is order-sensitive. Its existing `Match` requires equal title sets, no hard
    differences and `OrderMatches`. A title-order swap alone is a `Mismatch`, even if
    Chromium enumerates files in name order.
  - **Same identify experience** is measured independently in the versioned
    `differences.identifyExperience` result. It compares the actual default ordered rows,
    including duplicates, displayed chapter count, formatted size, length, playlist label,
    truncated segment map and full tooltip, conditional comment column and values,
    audio/subtitle/chapter labeling availability after identification, and ordered names in
    audio/subtitle labeling dialogs. The comparer and identify page share presentation helpers.
    Source aliases, row splits, missing angle rows and order changes remain UX differences;
    equal clips/bytes do not excuse them. Conversely, byte differences that do not change
    any displayed value need not change this metric. Missing metadata is compared as
    displayed by the production mapper, not ignored.
    Saved identifications/labels, user sorting/filtering, external chapter suggestions,
    clipboard state and visual layout are excluded. This is a data-level UX metric,
    not a screenshot or playback check. Different discs, empty row sets and scan errors
    are `NotComparable`. Historical comparisons without this metric are **not measured**,
    never inferred matches. Re-compare computes it from stored artifacts without rescanning.
    Manifest-to-identify projection translates known audio/subtitle codec names and explicit
    layouts into MakeMKV display vocabulary without changing semantic manifest fields.
    Blu-ray audio format codes expose mono/stereo only when explicitly declared; generic
    multichannel evidence does not establish 5.1, 7.1 or Atmos. No subtitle duplicates or
    embedded audio-core tracks are fabricated to improve parity.
  - Blu-ray artifact-consistency warnings report log sources missing from the scan inventory
    and standalone stream size disagreements. They do not change parity status or prove a
    different disc: the identity hash compares the scan with the contribution record, not
    independently with the MakeMKV log.
  - For a playlist whose manifest has explicit, timed alternate-angle groups, the comparer
    recognizes duplicate MakeMKV rows only when they form the complete set of distinct
    playback perspectives described by those groups. It compares chapter count and length,
    but deliberately does not compare size, display size or segment map: one manifest title currently
    represents all alternate clips while MakeMKV reports one selected perspective per row.
    Partial, repeated, or otherwise unaccounted-for rows remain hard mismatches. This does
    not resolve the manifest's angle-specific size or identification risk; selecting and
    sizing one perspective correctly needs a model/schema change rather than an inferred
    default angle.
  - A final Blu-ray entry mark in the existing 1.001-second terminal-sentinel window is
    excluded unless both its final chapter interval and distance to playlist end repeat
    the preceding authored chapter interval. Repeated spacing is evidence that the final
    mark is authored, including when all marks reference one PlayItem or when they cross
    PlayItems. The evidence-backed tolerance and the separate short-title start guard
    remain unchanged.
  - New manifests retain ordered scanner diagnostics in the schema-supported extension
    `thediscdb.optical-disc-manifest/scan-diagnostics`. The comparison detail page exposes them,
    including authored chapter marks, final chapter-mark ticks, sentinel exclusions, parse failures, duplicate playlist
    exclusions and per-stream promotion reasons. `ODM_BD_STREAM_TITLE_ATTRIBUTED` records
    playlist-to-stream attribution separately from `ODM_BD_STREAM_TITLE_PROMOTED`, including
    the original playlist/part, clip, duration evidence and removed chapter-mark count.
    These describe reconciliation decisions; later duplicate exclusions may remove a candidate.
    Older manifests remain supported.
- **Admin dashboard.** `/admin/disc-scan-parity` leads with graded similarity and also shows
  strict same-identify-experience match rates among measured comparisons, with separate
  not-comparable/not-measured coverage for both versions.
  Technical parity remains visible independently. It uses the latest comparison for
  each disc, broken down by format, producer version, MakeMKV version and browser.
  `/admin/disc-scan-parity/{discId}` shows the title-by-title differences, links to download the
  log, the manifest and the comparison file, and a Re-compare action. Re-compare all is on the
  dashboard. The admin pages read the comparison files for discs that have both uploads.
- **Publishing.** Approved contributions write `discNN.txt` and, when present, `discNN.odm.json`.
  The importer, the data-repo CI and the tools ignore `*.odm.json` when enumerating `disc*.json`.
- **CLI.** `thediscdb-scan` (`code/TheDiscDb.DiscScan.Cli`) produces the same manifest from a
  local disc on Windows, macOS and Linux. Packaging/publication mechanisms exist,
  but they do not prove a public release has been published; see below. The optional
  post-log CLI upload is gated by `ShowScanFileUpload`.

## Remaining rollout and publication work

- **Collect evidence and investigate mismatches** before enabling phase 2. Record
  results for the current producer version; a graded similarity score is not a
  substitute for the exact-match gate.
- **Publish and verify CLI packages.** As of 2026-10-29, the public GitHub release
  lists for both `TheDiscDb/web` and `TheDiscDb/optical-disc-manifest` are empty.
  This does not establish whether private draft releases exist.
  [`Build-ScanCli.ps1`](../scripts/Build-ScanCli.ps1) builds all six runtimes;
  [`Publish-ScanCli.ps1`](../scripts/Publish-ScanCli.ps1) targets
  `TheDiscDb/optical-disc-manifest` and creates a **draft** by default (`-Publish`
  makes it public). Smoke-test packages on their target platforms and verify
  checksums before declaring publication complete.
  The separate [tag-triggered workflow](../.github/workflows/publish-scan-cli.yml)
  creates releases in **web**, not in the destination configured by the local scripts.
- **Choose when to expose CLI-file uploads.** The flag exists and is false in
  source defaults; do not assume the documented download/upload path is public
  merely because the implementation exists.
- **Implement phase 3** separately: scan-first entry flow and retirement of logs.
- **Resolve remaining scanner limitations** with evidence: angle-specific
  identity/size/segment modeling, unsupported DVD multi/random PGC and multi-angle
  navigation, and unresolved playlist grouping/timing cases. General HDMV/BD-J
  navigation resolution is not supplied by the current playlist heuristics.

Already delivered: optional browser scans, stored comparison history, strict
identify parity, graded similarity, re-comparison/admin dashboards, and manifest
publication alongside approved contributions. The sibling tools repository also
ships [OdmScanner](https://github.com/TheDiscDb/tools/blob/main/tools/OdmScanner/README.md) for local capture,
comparison, and regression replay; it is not an unimplemented scanner/replay task.

## Graded identify similarity v1

`differences.identifyExperience.similarity` is optional and separately versioned.
It does **not** replace strict identify v1 or technical pass/fail. Historical strict
measurements without it remain strict measured / graded **Not measured**. Unknown
graded versions are not interpreted. Empty acquisitions, different discs and errors
have no score; absent references are not successful 0%/100% fallback measurements.

Alignment uses exact source identity (ordinal case-insensitive, trimmed source key;
DVD integer padding normalized only for identity). Different playlists, stream aliases,
split parts and equivalent clip sets are not interchangeable. For duplicates, the nth
occurrence on the log side pairs with the nth on the scan side, without choosing the
best-looking metadata match. Indistinguishable duplicate swaps cannot be detected as
ordering changes; metadata differences remain visible. Unknown/blank sources never pair.
Missing/extra lists preserve occurrences and acquisition row numbers.

Let `M` be aligned occurrences, `missing=L-M`, `extra=R-M`, and
`U=M+missing+extra`. Components are percentages, using unrounded counts:

| Component | Numerator / denominator | Weight |
|---|---|---|
| Choice coverage | `M / U` | 40% |
| Row presentation | Equal checks on aligned rows / `7U` | 25% |
| Ordering | Concordant aligned occurrence pairs / `U(U-1)/2` | 15% |
| Labeling | Equal checks on aligned rows / `7U` | 20% |

Presentation checks: displayed chapter count, formatted size, length, playlist label,
full segment map, comment-column visibility and displayed comment. Truncated segment-map
text and full tooltip get **one** vote (the full map determines both), unlike strict v1's
separate checks. Visibility is assessed for the whole acquisition and voted once per row.
Labeling checks: chapter/audio/subtitle action availability, audio/subtitle dialog counts,
and audio/subtitle ordered name-list equality. Counts and names are reported separately;
a count mismatch also makes the full ordered name-list check fail. Both-empty lists
agree on absence, not on invented names. All seven checks are fixed for every union slot.

Missing/extra occurrences fail all fourteen row/label checks and all ordering pairs
involving those occurrences. Unrelated inserted rows therefore do not cause positional
cascades, but omissions are never rewarded by shrinking to only matched denominators.
For the sole union choice (`U=1`), ordering is `1/1` if aligned (trivially ordered).
With no aligned sources all components are 0%, not vacuous 100%. Empty acquisitions
are not comparable. Ordering uses a Fenwick tree, O(n log n) time and O(n) memory.

Overall = `0.40 coverage + 0.25 presentation + 0.15 ordering + 0.20 labeling`.
There are no main-feature weights, caps or hidden confidence adjustments. Only display
rounds (one decimal); JSON retains full precision and component numerator/denominator.
The admin dashboard and breakdowns use the **arithmetic mean of measured per-disc**
overall/component percentages, not pooled rows. They show measured/not-comparable/
not-measured counts and total missing/extra occurrences. Disc lists/history show each
disc's overall, components and missing/extra counts; details include aligned field diffs
and missing/extra identities. A high score does not excuse a missing main choice or
establish playback correctness, UI layout fidelity or whole-corpus accuracy.

## Switching to phase 2

Switch `Contributions:DiscScanMode` to `Default` when the dashboard, filtered to the current
producer version, shows all of the following:

- at least 200 compared discs for each format (DVD, Blu-ray, UHD), from at least two browsers;
- an exact-match rate of at least 98% for each format;
- every remaining mismatch investigated and explained, for example by an old MakeMKV version, a
  different disc, or a known MakeMKV quirk that the identification flow tolerates.

In `Default` mode, `GetDiscLogs` uses the manifest first. A disc that already has identified items
keeps the log as its source, so that title indices don't shift.

## Investigating a mismatch

1. Open the disc on the parity dashboard and read the differences table.
2. Separate comparisons by producer version. Scans cannot be regenerated without the disc,
   because raw structure files are not stored. Re-compare only re-evaluates saved artifacts;
   it does not run the updated scanner. Validate generator fixes with future submissions,
   rather than requiring users to rescan.
3. Inspect artifact warnings and scanner diagnostics before changing chapter or stream rules.
   Do not mask hard differences with a duration tolerance or a blanket audio requirement.
   Multi-angle comparisons that are explicitly identified above have unverified per-angle
   size and segment-map parity until the manifest can represent the selected perspective.
4. Check the MakeMKV version. Logs from 1.15 and 1.16 (the CellTrim era) list zero-length DVD
   titles that newer versions skip.
5. Download both blobs. If the generator is wrong, add a fixture to
   `TheDiscDb.OpticalDiscParsers.Tests` and fix `OpticalDiscManifestGenerator`, then monitor
   comparisons from subsequent submissions using the fixed producer version.

## Generator maintenance

DVD title-playback flags are decoded in disc bit order: multi/random PGC is bit 6,
while chapter search and title/time play are bits 1 and 0. Chapter-search permission
must not disable timing, sizes or cell maps for a sequential title. True multi/random
PGC and multi-angle navigation remain unsupported; their missing metadata is reported
as `Unavailable (not recorded)` in comparisons, not a measured zero. Structural fake-title
checks still apply when all chapter/cell references are accounted for.

Sequential, single-angle Blu-ray playlists may split at non-seamless boundaries when
a shared primary audio/video PID has incompatible known codec, format or rate
declarations, or when complete, unambiguous known primary selections establish a
change in stream membership. Stream counts or language changes without that evidence
do not establish a split. Ambiguous CLPI program declarations, warning/error evidence,
subpaths, stills, random playback and stereoscopic relationships do not qualify.
Parts retain their original ordinal
(`source.part`) even if subsequent reconciliation attributes an earlier part to a
stream. Each part gets rebased segments/chapters, its own streams and clip sizes;
`ODM_BD_PLAYLIST_SPLIT` records the decision. This is not general HDMV/BD-J navigation
resolution, and non-seamless connections alone remain insufficient evidence.

Fresh scans also record informational `ODM_BD_PLAYLIST_AUTHORED_EVIDENCE` diagnostics
before any loop, tail or split rule: original play-item indices, clip/STC references,
in/out ticks, connection/still/angle flags, selected primary STN formats and CLPI
presentation bounds, individual ATC/STC ranges/ID offsets and program formats/packet
starts. Evidence is batched in groups of 16
play items without truncation. `ODM_BD_PLAYLIST_SPLIT_DECISION` records exclusion
guards or each evaluated boundary, including absent/unknown/ambiguous format evidence.
`ODM_BD_PLAYLIST_PRESENTATION_TIMING` records each mapped part's play-item ticks,
trailing-clip extension and resulting presentation ticks, plus rounded segment timing.
All tick values use 45 kHz; alternate-angle segments share playback time and must not
be summed as sequential content. These diagnostics use already parsed control files,
read no payload bytes and do not change title selection or timing.
Use them to investigate comparisons such as Gladiator Blu-ray 2671 (substantial
extra-duration differences) and Dune Blu-ray 2668 (unsplit title parts). Do not replace
authored ranges with full CLPI clip lengths without establishing why the ranges differ.
Existing stored manifests cannot acquire this evidence through Re-compare.

A final chapter at the exact start of a later play item is preserved when at least
one second remains, including the 45,045-tick case in comparison 2638. Subsecond
terminal tails and the existing repeated-interval exception retain their prior rules.
`ODM_BD_CHAPTER_PLAY_ITEM_START_PRESERVED` records this additional exception.

Comparisons explicitly report `TitleGrouping` when MakeMKV and the manifest describe
different part keys of the same playlist. This does not invent matches or suppress
log-only/scan-only entries: zero differences among exact-key matches does not establish
parity for unmatched features. Re-comparing stored artifacts can improve reporting,
but the generator changes above require a fresh physical scan to validate in production.

`OpticalDiscManifestGenerator` is the public orchestration entry point. Internal DVD and
Blu-ray builders own format-specific assembly; Blu-ray playlist rules, chapter/segment
mapping, stream projection and ordered title reconciliation are separate collaborators.
`DiscFileCatalog` retains directory enumeration order alongside a sorted manifest inventory
and case-insensitive file lookup. `ControlFileReader` owns bounded reads and backup selection:
a backup without parsed evidence must not replace a usable partial primary or be reported
as used. A usable partial backup remains eligible when the primary is unsuccessful.

Preserve reconciliation stage order, stream-evidence precedence, diagnostics and read metrics
when changing these collaborators. The parser fixture suite and characterization tests cover
directory order, backup failures, partial DVD joins and MPLS/CLPI stream precedence.

Single-clip chaptered playlists suppress a duplicate standalone stream only when the
presentation duration and byte size match and the stream adds no selected tracks.
Dolby Vision clips remain separate candidates. Multi-clip still playlists promote only
their actual infinite-still clips, and only when there are at most two distinct such
clips. This preserves Monsters University's two-still candidates without promoting
ordinary or finite-still play items alongside them. Larger galleries remain playlist
candidates but do not promote their clips as extra standalone choices;
`ODM_BD_STILL_GALLERY_STREAMS_SKIPPED` records the playlist and distinct infinite-still
clip count. The observed eight-playlist, 16-still-per-playlist gallery therefore avoids
128 extra stream choices. A gallery cut from one clip remains represented by its
playlist alone. These rules do not change independent orphan or Dolby Vision promotion.

Narrower trailing play items in ordinary sequential presentations are trimmed up
to one second (1.001 seconds at NTSC rates). Warning/error evidence, angles, stills,
subpaths, or non-sequential playback retain the older half-second rule; stereoscopic
playlists are preserved. Longer tails and equal stream tables remain intact.
The discarded clip may still be a standalone choice.
[`BluRayPlaylistRules`](../code/TheDiscDb.OpticalDiscManifest/Generation/BluRayPlaylistRules.cs)
is the implementation for both trimming and splitting. Generator changes affect
fresh scans/replays, not manifests already stored.

The unresolved playlist grouping/timing mismatches in comparisons 2813 and 2822 remain
mismatches pending further evidence; these UX-parity fixes do not change their scanner
grouping or timing behavior.

Standalone promotion excludes clips whose nonempty CLPI program and extension stream
declarations are entirely Interactive Graphics (`0x91`). The scanner records
`ODM_BD_STREAM_TITLE_MENU_ONLY_EXCLUDED` for these menu assets. Missing/empty metadata,
CLPI warning/error diagnostics, mixed stream declarations and unknown coding types
do not qualify for this exclusion; video-only candidates remain supported.

Do not add duration-based dropping for short standalone clips: production comparisons 2629
and 2630 contain video in candidates lasting 0.041689 and 5.046689 seconds. Also leave the
observed 1.001-second / 24,576-byte tail rule unchanged; its cause has not been established.

## Phase 3 (future)

- Move the disc scan before the TMDB lookup so that the content hash can find existing releases
  first.
- Remove the MakeMKV log upload paths and the PowerShell and Bash helpers.
- Tools already support manifest-only discs through `DiscInfoLoader`, which uses the log when
  present and the manifest otherwise.
