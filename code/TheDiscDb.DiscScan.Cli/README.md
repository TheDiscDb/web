# TheDiscDb Scan CLI

`thediscdb-scan` is an open-source, cross-platform command-line tool that creates TheDiscDb Optical Disc Manifest (`.odm.json`) files from a DVD, Blu-ray, or UHD Blu-ray mounted on your computer.

The tool reads disc structure metadata (`VIDEO_TS`, `BDMV`, `AACS`) and writes a manifest that can be uploaded to TheDiscDb. It does **not** upload anything itself.

## Download

Download the archive for your operating system from:

<https://github.com/TheDiscDb/optical-disc-manifest/releases?q=scan-cli>

Release artifacts are published for:

- Windows x64 and arm64
- Linux x64 and arm64
- macOS x64 and arm64

## Usage

```console
thediscdb-scan <path> --output disc01.odm.json
thediscdb-scan <path> --stdout > disc01.odm.json
thediscdb-scan --list
thediscdb-scan list
thediscdb-scan --help
```

By default, output is written to `<volume-label-or-folder-name>.odm.json` in the current directory. Use `--force` to overwrite an existing file.

Arguments, validation, help and version output are handled by System.CommandLine on Windows,
macOS and Linux. `-o` is an alias for `--output`, `-q` for `--quiet`, and `-v` for
`--verbose`. `--stdout` cannot be combined with `--output`, and `--quiet` cannot be
combined with `--verbose`. Use `--version` to print the scanner version.

Exit codes: `0` success, `2` bad arguments, `3` no disc found, `4` generation failure,
and `5` manifest validation failure.

### Windows

```powershell
.\thediscdb-scan.exe --list
.\thediscdb-scan.exe D: --output ".\disc01.odm.json"
.\thediscdb-scan.exe D:\ --force
```

### macOS

```bash
xattr -d com.apple.quarantine ./thediscdb-scan
chmod +x ./thediscdb-scan
./thediscdb-scan --list
./thediscdb-scan /Volumes/MY_DISC -o disc01.odm.json
```

### Linux

```bash
chmod +x ./thediscdb-scan
./thediscdb-scan --list
./thediscdb-scan /media/$USER/MY_DISC -o disc01.odm.json
```

## Uploading

Upload the generated `.odm.json` file on the **Disc Manifest** tab at <https://thediscdb.com/contribute>.

## Local release builds

Install the .NET 10 SDK and PowerShell 7.4 or newer. Run these commands from the
web repository root:

```powershell
pwsh -File scripts\Build-ScanCli.ps1 -Version 1.0.0
```

`scripts/scan-cli-release.json` configures the release repository, tag prefix,
build configuration and six target runtimes. The build script cross-publishes
self-contained, single-file executables for Windows, Linux and macOS (x64 and
ARM64), so users do not need .NET installed. Windows packages are ZIP files;
Linux/macOS packages are tar.gz files with executable permissions preserved,
even when built on Windows. Packages include the README and license.

Output goes to `artifacts/scan-cli/1.0.0`, with `sha256.txt` and a `release.json`
manifest. Existing output directories are refused to prevent mixing versions or
stale files. Use `-OutputDirectory` to choose a fresh location, or
`-RuntimeIdentifier win-x64` to build just one target for local testing.
Cross-publishing does not execute the foreign-platform binaries; test packages on
their target operating systems before publishing. macOS binaries are not signed
or notarized.

## Publishing a GitHub release

Install GitHub CLI (`gh`) and authenticate with `gh auth login` using an account
with release-write permission on `TheDiscDb/optical-disc-manifest`.

```powershell
pwsh -File scripts\Publish-ScanCli.ps1 `
    -ArtifactDirectory artifacts\scan-cli\1.0.0 `
    -TargetCommitish main -WhatIf

pwsh -File scripts\Publish-ScanCli.ps1 `
    -ArtifactDirectory artifacts\scan-cli\1.0.0 `
    -TargetCommitish main
```

The second command creates a **draft** release tagged `scan-cli-v1.0.0` in
`TheDiscDb/optical-disc-manifest` and uploads all six packages, checksums and the
release manifest. `-TargetCommitish` must name an existing branch or commit in
that destination repository (not the web repository); GitHub creates the tag
there if it does not already exist. The script does not push the CLI source.
It verifies every package and requires the full runtime set before uploading.

Use `-NotesFile` for custom release notes, `-Prerelease` for a preview, or
`-Publish` to make a new release public immediately instead of creating a draft.
Versions containing a prerelease suffix are automatically marked prerelease.
Existing releases and assets are not replaced. If an upload fails, inspect the
draft in GitHub before retrying or completing the upload manually.

The existing `publish-scan-cli.yml` workflow still publishes releases in the web
repository; these local scripts publish independently to optical-disc-manifest.

## Privacy

`thediscdb-scan` only reads local disc structure files and file sizes needed to describe the disc. It does not read full video payloads, does not contact TheDiscDb, and does not upload anything. You choose when and where to upload the generated `.odm.json` file.

## License

Apache-2.0, consistent with the TheDiscDb repository license.
