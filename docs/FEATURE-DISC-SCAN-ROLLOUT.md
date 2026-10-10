# Disc Scan Rollout (Replacing MakeMKV Logs)

## Overview

The browser disc scan produces an Optical Disc Manifest (ODM, `.odm.json`) that reproduces the
title list MakeMKV reports for DVDs, Blu-rays and UHDs. This plan rolls the scan out in three
phases so that MakeMKV logs can eventually be retired.

| Phase | State | Description |
|---|---|---|
| 1. Collect both | 🟢 Built | The MakeMKV log is required and drives identification. After the log uploads, an interstitial page (`/contribution/{id}/discs/{discId}/scan`) offers a browser scan, or lets contributors skip to identify. A scan-file upload (from the CLI) is also offered there when `Contributions:ShowScanFileUpload` is `true` (off by default). The server stores both and compares them. |
| 2. Scan by default | 🟢 Built, switched off | Set `Contributions:DiscScanMode` to `Default`. The scan becomes the first upload option and the identification source, and MakeMKV logs move to "Advanced". |
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
  - `OrderMatches` is tracked separately and never causes a mismatch on its own, because Chromium
    lists files in name order.
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
- **Admin dashboard.** `/admin/disc-scan-parity` shows match rates, using the latest comparison for
  each disc, broken down by format, producer version, MakeMKV version and browser.
  `/admin/disc-scan-parity/{discId}` shows the title-by-title differences, links to download the
  log, the manifest and the comparison file, and a Re-compare action. Re-compare all is on the
  dashboard. The admin pages read the comparison files for discs that have both uploads.
- **Publishing.** Approved contributions write `discNN.txt` and, when present, `discNN.odm.json`.
  The importer, the data-repo CI and the tools ignore `*.odm.json` when enumerating `disc*.json`.
- **CLI.** `thediscdb-scan` (`code/TheDiscDb.DiscScan.Cli`) produces the same manifest from a
  local disc on Windows, macOS and Linux. It is released by `.github/workflows/publish-scan-cli.yml`
  when a `scan-cli-v*` tag is pushed. Contributors upload its output on the Disc Manifest tab.

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

Sequential, single-angle Blu-ray playlists may split at non-seamless boundaries only
when a shared primary audio/video PID has incompatible known codec, format or rate
declarations. Stream-count or language changes alone never split titles. Ambiguous
CLPI program declarations, warning/error evidence, subpaths, stills, random playback
and stereoscopic relationships do not qualify. Parts retain their original ordinal
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
Dolby Vision and still-playlist clips remain separate candidates. Narrower trailing
play items use the NTSC half-second boundary (0.5005 seconds, rounded to 45 kHz ticks)
when their video rate declares 23.976, 29.97 or 59.94 fps; longer tails, equal stream
tables and stereoscopic playlists are preserved. These generator fixes affect future
submissions, not manifests already stored.

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
