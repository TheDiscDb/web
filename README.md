# TheDiscDB.com

Welcome to the official repository for [thediscdb.com](https://thediscdb.com) — a web-based cataloging tool designed to document the contents of physical movie discs (Blu-ray, UHD, and DVD). Built to complement [MakeMKV](https://makemkv.com), this site helps users identify and organize disc titles, chapters, and metadata.

---

## Local Development Setup

To run the site locally, you'll need the following tools installed:

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) configured to run Linux containers
- [Visual Studio 2022+](https://visualstudio.microsoft.com/) with ASP.NET and container development workloads
- [.NET SDK 9.0](https://dotnet.microsoft.com/en-us/download)

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

---

## Tech Stack

| Layer             | Technology             |
|-------------------|------------------------|
| Backend           | ASP.NET Aspire (.NET 9.0) |
| Frontend          | Blazor |
| IDE               | Visual Studio          |

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