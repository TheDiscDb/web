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
  - New manifests retain ordered scanner diagnostics in the schema-supported extension
    `thediscdb.optical-disc-manifest/scan-diagnostics`. The comparison detail page exposes them,
    including authored chapter marks, final chapter-mark ticks, sentinel exclusions, parse failures, duplicate playlist
    exclusions and per-stream promotion reasons. Older manifests remain supported.
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
4. Check the MakeMKV version. Logs from 1.15 and 1.16 (the CellTrim era) list zero-length DVD
   titles that newer versions skip.
5. Download both blobs. If the generator is wrong, add a fixture to
   `TheDiscDb.OpticalDiscParsers.Tests` and fix `OpticalDiscManifestGenerator`, then monitor
   comparisons from subsequent submissions using the fixed producer version.

## Generator maintenance

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

## Phase 3 (future)

- Move the disc scan before the TMDB lookup so that the content hash can find existing releases
  first.
- Remove the MakeMKV log upload paths and the PowerShell and Bash helpers.
- Tools already support manifest-only discs through `DiscInfoLoader`, which uses the log when
  present and the manifest otherwise.
