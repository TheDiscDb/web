# BDMV parser implementation notes

## Scope

This change adds a parser foundation for optical disc navigation metadata:

| Family | Files | Parser | Status |
|---|---|---|---|
| DVD-Video | `VIDEO_TS.IFO`, `VIDEO_TS.BUP`, `VTS_*.IFO`, `VTS_*.BUP` | `DvdIfoParser` | VMGI TT_SRPT logical titles; VTSI VTS_PTT_SRPT chapter-to-PGC mapping; VTS_PGCIT, PGC stream controls, PGC program maps, cell playback records, and cell position records |
| Blu-ray index | `index.bdmv` | `BdmvParser.ParseIndexAsync` | Header, AppInfoBDMV, first-play, top-menu, title references |
| Blu-ray MovieObject | `MovieObject.bdmv` | `BdmvParser.ParseMovieObjectsAsync` | Object flags and HDMV command fields |
| Blu-ray CLPI | `*.clpi` | `ClpiParser` | Header offsets, ClipInfo authored packet/rate fields, SequenceInfo STC timing, ProgramInfo stream attributes, CPI summaries |
| Blu-ray MPLS | `*.mpls` | `MplsParser` | Header offsets, AppInfoPlayList, play items, STN stream tables, subpaths and SubPlayItems, playlist marks |

The implementation intentionally parses navigation metadata only. It does not parse media payloads, AACS data, encrypted content, or disc keys.

## Fixture provenance and hashes

Fixtures are copied from the local TheDiscDb sample corpus:

- local BDMV, CLPI, MPLS, and DVD IFO control-file samples
- existing DVD IFO fixtures in the test fixture tree

See:

- `TheDiscDb.OpticalDiscParsers/fixtures/PROVENANCE.md`
- `TheDiscDb.OpticalDiscParsers.Tests/fixtures/MANIFEST.sha256`

The manifest contains SHA-256 hashes for every committed parser fixture.

## Clean-room reference documentation

The binary layouts were implemented from public, reverse-engineered structure information and direct inspection of TheDiscDb-owned/authorized fixtures. Public reference points used for field alignment:

- libbluray navigation parser source for BDMV index/MovieObject/CLPI/MPLS structure boundaries:
  - `src/libbluray/bdnav/index_parse.c`
  - `src/libbluray/hdmv/mobj_parse.c`
  - `src/libbluray/bdnav/clpi_parse.c`
  - `src/libbluray/bdnav/mpls_parse.c`
  - `src/libbluray/bdnav/extdata_parse.c`
- libdvdread public DVD IFO structure definitions for VMGI/VTSI table pointer and record layout alignment:
  - `src/dvdread/ifo_types.h`
- pyparsebluray and MediaPortal/libbluray-derived public source for MPLS ExtensionData descriptors, extension SubPath entries, and STN SS stereoscopic stream metadata alignment.
- Local fixture hex inspection for validating offsets, lengths, version identifiers, and representative stream codes.

No proprietary DVD/Blu-ray specification text, disc keys, decrypted media, or third-party payload streams were used.

## Support matrix

| Area | Supported now | Known limitations |
|---|---|---|
| BDMV index | `INDX0200`, `INDX0300`, AppInfo, HDMV/BD-J title refs | Extension data is not deeply interpreted beyond offsets; BD-J app resources are not loaded |
| MovieObject | `MOBJ0200`, `MOBJ0300`, object flags, command bitfields | Commands are exposed structurally; no HDMV VM execution or semantic decompilation |
| CLPI | `HDMV0200`, `HDMV0300`, ClipInfo source-packet count and recording-rate fields, ATC/STC timing summary, ProgramInfo streams, CLPI extension-declared stereoscopic streams, raw stream attributes, ISRC, CPI summaries | EP-map coarse/fine entries are summarized but not expanded into full seek tables; CLPI does not embed the `xxxxx` file stem used to pair `xxxxx.clpi` with `STREAM/xxxxx.m2ts`; most CLPI extension entry types are preserved only by diagnostics/known-safe skips |
| MPLS | `MPLS0200`, `MPLS0300`, play items, primary and alternate-angle clip refs, stream tables, SubPath/SubPlayItem records, ExtensionData descriptors, stereoscopic extension SubPaths, STN SS dependent-view stream metadata, explicit MVC base/dependent-view relationships, playlist marks | PiP metadata, static metadata, and most non-3D extension blocks are preserved as bounded unsupported metadata but not deeply interpreted |
| DVD IFO | `DVDVIDEO-VMG`, `DVDVIDEO-VTS`, TT_SRPT logical titles, VTS_PTT_SRPT PTT-to-`(PGCN, PGN)` maps, VTS_PGCIT PGC search pointers, PGC audio/subpicture control records, program maps, cell playback records, cell position records, stream attributes | Menu PGC interpretation, VM command semantics, complete language/unit tables, detailed channel-layout normalization, and DVD conformance across broader multi-angle/seamless-branching fixture sets remain future work |
| Backup metadata | DVD `.BUP` fallback and Blu-ray `BDMV/BACKUP/*` fallback for index, MovieObject, CLPI, and MPLS control files | Fallback records diagnostic evidence; media payload backup/recovery is intentionally out of scope |
| WebAssembly | Parser project builds as a dependency of `TheDiscDb.Client` | Fixture execution currently runs under .NET tests; WASM runtime parser execution should be added when integrated into the scanner UI |

## CLPI-backed standalone clip evidence

`ClpiParser` exposes browser-safe authored evidence suitable for creating standalone clip-candidate records outside the parser layer, without opening `STREAM/*.m2ts` payload files:

- ClipInfo:
  - clip stream type and application type;
  - ATC-delta flag;
  - transport-stream recording-rate field;
  - number of source packets;
  - TS type validity and format identifier when present.
- SequenceInfo:
  - ATC sequence start packet numbers;
  - STC sequence PCR PID, STC start packet number, presentation start time, and presentation end time;
  - `ClpiPresentationSummary` with earliest start time, latest end time, duration in 45 kHz ticks, and contributing STC sequence count.
- ProgramInfo:
  - program sequence start packet number;
  - PMT PID;
  - stream group count;
  - per-stream PID, category, coding type, raw attribute length, raw attribute bytes, format code, rate code, aspect code, language code, character code, and ISRC when present;
  - HEVC-specific raw flags exposed by CLPI: CR flag, dynamic-range type code, color-space code, and HDR10+ flag.
- CPI:
  - entry-map PID, stream type, coarse/fine entry counts, and EP-map start address summary.

Fields that still require payload inspection, stream decoding, or profile-specific extension parsing are intentionally not inferred from CLPI: exact HEVC profile/level, Dolby Vision profile and RPU presence, bitrate, frame-accurate GOP details, forced-subtitle variants, and decoded subtitle contents.

Standalone clip association is a Blu-ray filesystem convention, not an embedded CLPI field: an ODM mapper can pair `BDMV/CLIPINF/xxxxx.clpi` with `BDMV/STREAM/xxxxx.m2ts` by the shared five-character file stem while still using only CLPI control-file bytes for timing and stream evidence.

For 3D dependent-view clips, CLPI `ExtensionData` entry `2.5` may declare stereoscopic stream attributes even when standard ProgramInfo has zero streams. The parser exposes those records through `ClpiFile.ExtensionStreams`; the `BD-3D/00301.clpi` fixture includes PID `0x1012`, coding type `0x20` (`MVC Video`), format code `6`, and rate code `1`.

## MPLS ExtensionData and MVC dependent views

`MplsParser` now parses the generic MPLS `ExtensionData` container:

- declared extension length;
- extension data-block start address;
- 12-byte extension descriptors with type id, version id, relative start address, and length;
- bounded raw metadata for unsupported entries;
- diagnostics for truncated headers, truncated descriptor tables, out-of-bounds entries, overlapping entries, truncated SubPath extension payloads, and truncated STN SS stream records.

Supported extension entries:

| Type/version | Parser name | Exposed evidence |
|---|---|---|
| `2.1` | STN SS extension | Dependent-view stereoscopic stream records, including stream type, SubPath/SubClip ids, PID, coding type, format code, rate code, raw attributes, and offset-sequence count |
| `2.2` | SubPath entries extension | Extension SubPath/SubPlayItem records, including SS Video SubPath type `8`, dependent clip id, codec id, STC id, in/out times, sync PlayItem id, sync PTS, and referenced clips |

When both entries are present, the parser creates `MplsStereoVideoRelationship` records. For the `BD-3D/00800.mpls` fixture, this exposes base clip `00300` with dependent MVC clip `00301` as relationship type `3d-dependent-view`; the playlist remains a single non-multi-angle PlayItem and the dependent view is not modeled as an alternate angle or logical title.

SSIF is a filesystem presentation for interleaved stereoscopic streams. The parser does not inspect `.ssif` or `.m2ts` payloads and does not infer payload-derived facts such as exact profile/level, Dolby Vision data, bitrate, decoded frame layout, or forced-subtitle variants.

## MPLS playlist mark semantics

`MplsPlaylistMark.MarkType` is the raw PlayListMark type byte. Public navigation definitions use:

- `0x01` (`MplsPlaylistMark.EntryMarkType`) for entry/chapter marks;
- `0x02` (`MplsPlaylistMark.LinkMarkType`) for link marks.

Fixture observation: all currently committed MPLS fixtures contain only mark type `0x01` for parsed marks. The ODM chapter mapper should count chapter marks with `MarkType == MplsPlaylistMark.EntryMarkType` (or `IsEntryMark`) and should not treat link marks (`0x02`) as chapters.

3D fixture observation: `BD-3D/00800.mpls` has 15 authored entry marks. The final entry mark is 11,262 ticks at 45 kHz before the PlayItem end. The parser intentionally exposes all authored marks accurately; MakeMKV-style chapter-end filtering belongs in ODM mapping or presentation policy, not in the parser.

## Manifest-mapping and schema-change proposals

The current parsers expose low-level records suitable for a later normalization layer. Proposed next schema/mapping steps:

1. Add a normalized optical-disc manifest model that maps:
   - DVD VMGI TT_SRPT titles to VTS title numbers.
   - DVD VTS_PTT_SRPT chapter entries to PGC/program numbers and then to PGC program/cell records.
   - BDMV `index.bdmv` titles to logical disc titles, once HDMV/BD-J object resolution is implemented.
   - MPLS play items to ordered playlist candidate segments, including alternate-angle clips.
   - CLPI ProgramInfo streams to audio/video/subtitle stream declarations.
   - MPLS PlayListMark entries to chapters.
2. Link MPLS clip IDs (`00001`) to CLPI files (`00001.clpi`) and stream payload paths (`STREAM/00001.m2ts`) without reading media payload bytes.
3. Add schema fields for:
   - `NavigationFormat` (`DVD-Video`, `Blu-ray`, `UHD Blu-ray`)
   - playlist id / clip id / stream pid references
   - raw codec code plus normalized codec name
   - 45 kHz tick timing plus `TimeSpan`
   - parser diagnostics and unsupported-feature flags
4. Add deterministic JSON snapshot tests for the normalized manifest after the mapping layer exists.

## Validation results

Validation performed before sharing this branch:

| Validation | Command | Result |
|---|---|---|
| Parser tests (.NET) | `dotnet test TheDiscDb.OpticalDiscParsers.Tests\TheDiscDb.OpticalDiscParsers.Tests.csproj /p:NuGetAudit=false --no-restore` | Passed: 50/50 |
| Blazor WebAssembly build | `dotnet build TheDiscDb.Client\TheDiscDb.Client.csproj /p:NuGetAudit=false --no-restore` | Passed: 0 warnings, 0 errors |
| Full solution build (.NET) | `dotnet build TheDiscDb.slnx /p:NuGetAudit=false --no-restore` | Blocked by a running `TheDiscDb` process locking output DLLs; targeted parser tests and Blazor client build passed |
