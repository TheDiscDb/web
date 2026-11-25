# TheDiscDB.com

Welcome to the official repository for [thediscdb.com](https://thediscdb.com) � a web-based cataloging tool designed to document the contents of physical movie discs (Blu-ray, UHD, and DVD). Built to complement [MakeMKV](https://makemkv.com), this site helps users identify and organize disc titles, chapters, and metadata.

---

## Local Development Setup

To run the site locally, you'll need the following tools installed:

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) configured to run Linux containers
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) with ASP.NET and container development workloads
- [.NET SDK 10.0](https://dotnet.microsoft.com/en-us/download)

### Getting Started

1. **Clone the repositories**  
   ```
   git clone https://github.com/TheDiscDb/web.git
   git clone https://github.com/TheDiscDb/data.git
   ```
   The data repository is used to seed the local database with items

2. **Configure Database Migration** 

	Edit `/code/TheDiscDb.DatabaseMigration/appsettings.json` with the path to your cloned data repo above. You can also the `MaxItemsToImportPerMediaType` to change the number of items that are seeded in the database. Note: Larger numbers will cause the site to startup to take longer the first time

3. Open `/code/TheDiscDb.sln` in Visual Studio
4. With the `TheDiscDb.AppHost` project set as the startup project - start the project in Visual Studio. The first time you run it may take a while to start up while Aspire downloads containers and the database is seeded.

**TODO: Provide a command line only way to clone and run the site**

### Six Labors license

Release builds require a valid Six Labors license. For local builds, set the
`SixLaborsLicenseFile` environment variable to the absolute path of your
`sixlabors.lic` file. License files are ignored by Git and must not be committed.

The **Build and deploy Blazor app** GitHub Actions workflow reads the
`SIXLABORS_LICENSE` secret from the `production` environment (or repository
secrets). Store the complete license file contents in that secret. The workflow
writes it to the runner's temporary directory, exposes its path to MSBuild, and
removes the file after publishing, including when a build or test fails.

### Release import behavior

The contribution Release Details screen requires title metadata from TMDB before
it enables the release form. Failed or empty lookups show a retry option and a
link back to title search, without discarding the saved disc scan. Retrying
restores the release draft after metadata loads successfully.

Imports merge a media item's releases by slug. A contribution workspace may contain
only one release, so releases absent from an import are retained; deleting a release
requires the explicit release-deletion workflow. Disc, title, and track updates
within an imported release retain their existing reconciliation behavior.

ContributionBuddy can explicitly opt into a new-release-only import for an existing
parent. This scope also limits asset and database processing; parent metadata, groups,
latest release date, and complete search identifiers are maintained. Sibling search
documents receive field-only updates when inherited groups change. Existing-release
updates, online admin imports, full-folder imports, box sets, and database seeding
retain their existing behavior.

If releases were overwritten by an earlier import, deploy the corrected importer
and re-import the media item's complete directory from the data repository.
The site's media-item detail cache can retain the old result for up to 12 hours;
restart the web app after repair to refresh it immediately.

### Search index rebuilds

Full search rebuilds load 100 parent items at a time using untracked split queries,
including only mapped titles, and upload up to 1,000 documents per request. Incremental
imports retain their existing batch size. The admin rebuild endpoint still runs
synchronously and deletes/recreates the active index before uploading documents;
avoid concurrent rebuilds and check the returned success/error summary.

---

## Search by disc

The desktop navigation's disc icon to the right of the search box opens
`/search/disc`, with a **Search by disc** tooltip.
Link directly to `/search/disc?dischash=<disc-content-hash>` to search a known
hash without selecting a folder. Omitting `dischash` keeps the folder-scanning
workflow. Hash-only searches show the same results; select the disc folder
before submitting the disc or adding a release.
Results hide the scanning instructions and folder picker. Use the results'
scan/search link to return to the folder-scanning workflow.
Insert a DVD, Blu-ray, or UHD disc and select its root folder (containing
`VIDEO_TS` or `BDMV`). The browser needs explicit folder permission; it cannot
automatically inspect the drive. Chrome and Edge support direct folder access,
with an alternative folder picker for restricted folders and other browsers.

The search calculates the existing disc content hash and links to matching movie,
series, and boxset releases, with front-cover thumbnails when available.
Searching is public and does not upload video or generate a full manifest.
When a folder scan matches a disc with missing identifiers, it automatically creates
a pending suggested edit containing only the missing Disc ID and/or Matrix256
fingerprint. Existing identifiers (including those stored on a shared disc's sibling
release) are preserved, and identical pending suggestions are not submitted again.
Hash-only searches cannot backfill identifiers. Signed-in scans use the current user's
ID; anonymous scans use `EditSuggestions:AnonymousDiscSearchUserId`, which defaults to
`anonymous-disc-search`. This is a dedicated attribution ID, not a login account.
The public identifier endpoint is limited to ten requests per minute per IP.
**Submit this disc** prepares a
full browser-local scan only after a no-match result. **Add a new release** reuses
a matching movie/series disc; boxset-only matches use a full scan and movie/series
title selection. Login is required only when continuing into a contribution.
Keep the disc inserted and the page open until scan preparation finishes.
The navigation button is hidden below the desktop breakpoint.

---

## Tech Stack

| Layer             | Technology             |
|-------------------|------------------------|
| Backend           | ASP.NET Aspire (.NET 10.0) |
| Frontend          | Blazor |
| IDE               | Visual Studio          |

---

## Performance Baseline Script

Use the script below to capture repeatable homepage and infinite-scroll latency metrics before and after changes:

```powershell
pwsh .\scripts\measure-homepage-performance.ps1 -BaseUrl https://localhost:7443
```

It writes a timestamped JSON result under `web\scripts\` and prints median/p95 timings for:

- `/`, `/movies`, `/series` document fetches
- GraphQL first-page fetch for movies and series
- GraphQL sequential paging (scroll simulation) for movies and series

To compare a new run against a prior baseline:

```powershell
pwsh .\scripts\measure-homepage-performance.ps1 -BaseUrl https://localhost:7443 -CompareTo .\scripts\homepage-perf-YYYYMMDD-HHMMSS.json
```

---

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

## Contributing

We welcome contributions! Feel free to:

- Fork the repository
- Submit pull requests
- Open issues for bugs or feature requests

Please follow our [contribution guidelines](CONTRIBUTING.md) if available.

---

## Contact

For questions, feedback, or partnership inquiries, reach out via [web@thediscdb.com](mailto:web@thediscdb.com).