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

### Release import behavior

Imports merge a media item's releases by slug. A contribution workspace may contain
only one release, so releases absent from an import are retained; deleting a release
requires the explicit release-deletion workflow. Disc, title, and track updates
within an imported release retain their existing reconciliation behavior.

If releases were overwritten by an earlier import, deploy the corrected importer
and re-import the media item's complete directory from the data repository.
The site's media-item detail cache can retain the old result for up to 12 hours;
restart the web app after repair to refresh it immediately.

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