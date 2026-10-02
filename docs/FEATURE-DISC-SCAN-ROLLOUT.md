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
  when both blobs exist, through `IDiscScanComparisonService`. Every run is stored as a
  `UserContributionDiscComparison` row.
  - Disc identity is checked first using the content hash; a scan of another disc is reported as
    `DifferentDisc`.
  - Titles are matched by source key: the playlist or stream file on Blu-ray, the title number on
    DVD.
  - Chapters, size, length and segment map are hard differences. Display size is a soft difference.
  - `OrderMatches` is tracked separately and never causes a mismatch on its own, because Chromium
    lists files in name order.
- **Admin dashboard.** `/admin/disc-scan-parity` shows match rates, using the latest comparison for
  each disc, broken down by format, producer version, MakeMKV version and browser.
  `/admin/disc-scan-parity/{discId}` shows the title-by-title differences, links to download both
  blobs, and a Re-compare action. Re-compare all is on the dashboard.
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
2. Ignore comparisons from old producer versions. Scans cannot be regenerated without the disc,
   because raw structure files are not stored.
3. Check the MakeMKV version. Logs from 1.15 and 1.16 (the CellTrim era) list zero-length DVD
   titles that newer versions skip.
4. Download both blobs. If the generator is wrong, add a fixture to
   `TheDiscDb.OpticalDiscParsers.Tests` and fix `OpticalDiscManifestGenerator`, then Re-compare once
   a fixed scan is uploaded.

## Phase 3 (future)

- Move the disc scan before the TMDB lookup so that the content hash can find existing releases
  first.
- Remove the MakeMKV log upload paths and the PowerShell and Bash helpers.
- Tools already support manifest-only discs through `DiscInfoLoader`, which uses the log when
  present and the manifest otherwise.
