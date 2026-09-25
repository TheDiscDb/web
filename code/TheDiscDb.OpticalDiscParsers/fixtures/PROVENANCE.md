# Optical Disc Parser Test Fixtures — Provenance

## DVD-Video IFO Fixtures

This file documents all binary fixtures used for testing the optical disc parsers.
For each fixture, we record: source, licensing, format scope, SHA-256, and expected normalized output.

### Best In Show

#### VIDEO_TS.IFO (VMGI)
- **Source:** B:\code\thediscdb\IFO\Best In Show\VIDEO_TS.IFO
- **Size:** 10,240 bytes
- **SHA-256:** 3A06E7512963BB7631C2DFA7BF21028A9F5BD602B33C10B271FE0663AA038...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** VMGI (Video Manager Information) header; identifies disc, volume, and regions
- **Purpose:** Minimal VMGI fixture; basic header structure validation
- **Expected Output:** See tests/fixtures/best-in-show-video_ts-expected.json

#### VTS_01_0.IFO (VTSI - Title Set 1)
- **Source:** B:\code\thediscdb\IFO\Best In Show\VTS_01_0.IFO
- **Size:** 73,728 bytes
- **SHA-256:** B094804BAFD0A75F7E81A95C7453BC6D1E3A29F166477AEE1747A7ACF4EB9...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** VTSI (Video Title Set Information); title 1 structure with program chains, cells, stream metadata
- **Purpose:** Representative single-title VTSI with multiple programs and streams
- **Expected Output:** See tests/fixtures/best-in-show-vts01-expected.json

#### VTS_02_0.IFO (VTSI - Title Set 2)
- **Source:** B:\code\thediscdb\IFO\Best In Show\VTS_02_0.IFO
- **Size:** 49,152 bytes
- **SHA-256:** 2EE772A710F51346F921EA93FD6EB926C4CC80C696BCC44296FF45579A826...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** VTSI for title 2; typically bonus content or supplementary features
- **Purpose:** Multi-title coverage; verifies title-set relationships in VMGI
- **Expected Output:** See tests/fixtures/best-in-show-vts02-expected.json

### Reservoir Dogs

#### VIDEO_TS.IFO (VMGI)
- **Source:** B:\code\thediscdb\IFO\Reservoir Dogs\VIDEO_TS.IFO
- **Size:** 10,240 bytes
- **SHA-256:** 581B19363B0A64CC0DCAEC1576AC8FC7394A4C76731F2D06B1FE25B287906...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** VMGI; 4-title DVD with region coding
- **Purpose:** Multi-title VMGI fixture
- **Expected Output:** See tests/fixtures/reservoir-dogs-video_ts-expected.json

#### VTS_01_0.IFO (VTSI - Title Set 1)
- **Source:** B:\code\thediscdb\IFO\Reservoir Dogs\VTS_01_0.IFO
- **Size:** 71,680 bytes
- **SHA-256:** 7A76C59CFB58910F36BB509812FA7CBCB501816FD39ECD2E03C32401FB4E8...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Main feature title with multiple audio/subtitle tracks
- **Purpose:** Feature film with rich stream metadata (multiple languages, formats)
- **Expected Output:** See tests/fixtures/reservoir-dogs-vts01-expected.json

#### VTS_02_0.IFO (VTSI - Title Set 2)
- **Source:** B:\code\thediscdb\IFO\Reservoir Dogs\VTS_02_0.IFO
- **Size:** 81,920 bytes
- **SHA-256:** BD9ED53A8D45D2C99029C9841E3DC162412FC50D913F7554497F215FE75BB...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Alternative version or alternate angles
- **Purpose:** Multi-angle or alternate version coverage
- **Expected Output:** See tests/fixtures/reservoir-dogs-vts02-expected.json

#### VTS_03_0.IFO (VTSI - Title Set 3)
- **Source:** B:\code\thediscdb\IFO\Reservoir Dogs\VTS_03_0.IFO
- **Size:** 14,336 bytes
- **SHA-256:** FF196F6C6C280D28EA364E8D5B7DEE82EFDB6DE8762A7AFD0B4670656331B...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Bonus content; simpler structure (fewer streams, programs)
- **Purpose:** Boundary condition testing; minimal VTSI
- **Expected Output:** See tests/fixtures/reservoir-dogs-vts03-expected.json

#### VTS_04_0.IFO (VTSI - Title Set 4)
- **Source:** B:\code\thediscdb\IFO\Reservoir Dogs\VTS_04_0.IFO
- **Size:** 14,336 bytes
- **SHA-256:** 92E753289CFE704B1C658A11763709AD8EBF50235F522AF65B182E67C6108...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Additional bonus content
- **Purpose:** Further coverage of optional/minimal title sets
- **Expected Output:** See tests/fixtures/reservoir-dogs-vts04-expected.json

### The Goonies

#### VIDEO_TS.IFO (VMGI)
- **Source:** B:\code\thediscdb\IFO\The Goonies\VIDEO_TS.IFO
- **Size:** 10,240 bytes
- **SHA-256:** 9760B681757A4C18E43FEAA79677F9C7AF69AD5F6D79D1FFBC252FFBF1D07...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** VMGI with 2-title structure
- **Purpose:** Additional multi-title VMGI fixture for regression testing
- **Expected Output:** See tests/fixtures/the-goonies-video_ts-expected.json

#### VTS_01_0.IFO (VTSI - Title Set 1)
- **Source:** B:\code\thediscdb\IFO\The Goonies\VTS_01_0.IFO
- **Size:** 131,072 bytes
- **SHA-256:** 9451421CE784104CBAEECE55538F0041E62963B44C12CC7FB16B85F837911...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Largest fixture in set; comprehensive program/chapter structure
- **Purpose:** Stress testing; largest title with complex cell references
- **Expected Output:** See tests/fixtures/the-goonies-vts01-expected.json

#### VTS_02_0.IFO (VTSI - Title Set 2)
- **Source:** B:\code\thediscdb\IFO\The Goonies\VTS_02_0.IFO
- **Size:** 34,816 bytes
- **SHA-256:** 32C5F6F29998653F64624359F6D87FCBEE795DCB5859462EC90DFB0C110DB...
- **License/Permission:** Available in TheDiscDb repository for testing
- **Format/Scope:** Bonus/supplementary content
- **Purpose:** Mid-size fixture coverage
- **Expected Output:** See tests/fixtures/the-goonies-vts02-expected.json

### Fight Club

- **Source:** Control files copied from mounted volume `FIGHTCLB` (Fight Club 1999 DVD, UDF, 8,194,308,096 bytes): `VIDEO_TS.IFO`, `VTS_01_0.IFO`..`VTS_05_0.IFO` only.
- **Scope:** Navigation metadata only; no `.VOB` payloads or `.BUP` backups committed. SHA-256 hashes are recorded in `TheDiscDb.OpticalDiscParsers.Tests/fixtures/MANIFEST.sha256`.
- **Purpose:** 59-cell/37-program sequential main feature (VTS 2) whose IFO cell sector ranges sum exactly to MakeMKV's 7,932,198,912-byte title size, plus multi/random-PGC bonus titles (VTS 1, 3, 4, 5) for which DVD title timing and size are intentionally omitted.

---

## Blu-ray/UHD Fixtures

Blu-ray/UHD parser fixtures are copied from local sample navigation metadata under:

- `B:\code\thediscdb\BDMV`
- `B:\code\thediscdb\CLPI`
- `B:\code\thediscdb\Mpls`

The committed copies live under `TheDiscDb.OpticalDiscParsers.Tests/fixtures` and include only navigation metadata:

| Fixture set | Count | Formats | Committed path |
|---|---:|---|---|
| BDMV root metadata | 4 | `index.bdmv`, `MovieObject.bdmv` | `fixtures/BDMV/**` |
| Clip information | 58 | `.clpi` | `fixtures/CLPI/**` |
| Playlists | 65 | `.mpls` | `fixtures/MPLS/**` |

### Permissions and Scope

- **Source:** The fixtures were supplied from the local TheDiscDb sample corpus above for parser development and regression tests.
- **Permission:** These are treated as TheDiscDb-owned or TheDiscDb-authorized internal test fixtures. Do not add third-party navigation files unless their redistribution permission is documented here first.
- **Scope:** Navigation metadata only. These fixtures do not include media payload streams (`.m2ts`), decrypted content, AACS keys, title keys, volume keys, or secrets.
- **Integrity:** Full SHA-256 hashes for all committed fixtures are recorded in `TheDiscDb.OpticalDiscParsers.Tests/fixtures/MANIFEST.sha256`.

### Blu-ray/UHD Sample Coverage

- `Hell on Wheels Disc 1`: Blu-ray `0200` navigation metadata with HDMV index and multiple short/feature clip and playlist files.
- `Project Hail Mary`: UHD/BDMV `0300` navigation metadata with BD-J index, HEVC streams, large feature CLPI files, and feature MPLS playlists.
- `Avengers Age of Ultron 3D`: Blu-ray `0200` control-file fixtures from mounted volume `MARVELS_AVENGERS_AGE_OF_ULTRON`; includes `00800.mpls` and dependent-view `00301.clpi` only, for parser coverage of MVC stereoscopic extension metadata without committing `.m2ts` or `.ssif` payloads.

---

## Fixture Acquisition and Usage Policy

### Redistribution Licensing
- **TheDiscDb IFO fixtures:** Available under TheDiscDb repository license; safe for internal testing.
- **All new fixtures:** Must have documented redistribution permission before use.
- **Prohibited:** Copyrighted media payloads, decrypted content, title keys, volume keys, or secrets.

### Testing Workflow
1. Each fixture has a corresponding expected-output JSON file that documents:
   - Parsed header values (VMGI/VTSI identifiers, counts, offsets)
   - Title/program/cell structure
   - Stream declarations
   - Any parsing errors/warnings

2. Fixture tests are run in .NET. Blazor WebAssembly compatibility is validated by building `TheDiscDb.Client`.
   - Determinism test proposal: Verify serialized parser output is byte-identical across runtimes once parser APIs are integrated into the WASM scanner path.
   - Regression test: Compare parsed fixture structures against expected assertions to detect parser changes.

3. For each fixture, generate and commit:
   - `fixtures/<disc-name>-<ifo-name>-expected.json` (expected normalized output)
   - `fixtures/<disc-name>-<ifo-name>.bin.sha256` (for integrity verification)

### Future Fixture Strategy
- Prioritize public domain, vendor-provided, TheDiscDb-owned, or explicitly authorized samples.
- Maintain separate confidential fixtures list for internal reference (not committed).
- Use mutation testing to generate derived fixtures for boundary and fuzz testing.
