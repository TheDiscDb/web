# TheDiscDb Scan CLI

`thediscdb-scan` is an open-source, cross-platform command-line tool that creates TheDiscDb Optical Disc Manifest (`.odm.json`) files from a DVD, Blu-ray, or UHD Blu-ray mounted on your computer.

The tool reads disc structure metadata (`VIDEO_TS`, `BDMV`, `AACS`) and writes a manifest that can be uploaded to TheDiscDb. It does **not** upload anything itself.

## Download

Download the archive for your operating system from:

<https://github.com/TheDiscDb/web/releases?q=scan-cli>

Release artifacts are published for:

- Windows x64 and arm64
- Linux x64 and arm64
- macOS x64 and arm64

## Usage

```console
thediscdb-scan <path> --output disc01.odm.json
thediscdb-scan <path> --stdout > disc01.odm.json
thediscdb-scan --list
```

By default, output is written to `<volume-label-or-folder-name>.odm.json` in the current directory. Use `--force` to overwrite an existing file.

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

## Privacy

`thediscdb-scan` only reads local disc structure files and file sizes needed to describe the disc. It does not read full video payloads, does not contact TheDiscDb, and does not upload anything. You choose when and where to upload the generated `.odm.json` file.

## License

Apache-2.0, consistent with the TheDiscDb repository license.
