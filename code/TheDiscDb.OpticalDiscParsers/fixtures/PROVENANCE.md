# Optical Disc Parser Test Fixtures

The parser tests use a small, anonymized set of optical-disc navigation files.
Fixture directory names identify only format and sample role; they do not identify
the source title or release.

## DVD-Video

| Fixture directory | Contents | Coverage |
|---|---|---|
| `DVD-A` | `VIDEO_TS.IFO`, two `VTS_*.IFO` files | Multi-title VMGI, title-set mapping, chapter and stream tables |
| `DVD-B` | `VIDEO_TS.IFO`, four `VTS_*.IFO` files | Title-size sector sums, aspect-ratio variants, stream attributes |
| `DVD-C` | `VIDEO_TS.IFO`, two `VTS_*.IFO` files | Chapter/cell structures, 4:3 NTSC and line-21 caption fields |
| `DVD-D` | `VIDEO_TS.IFO`, five `VTS_*.IFO` files | Sequential and multi/random-PGC title behavior, subtitle coding, line-21 captions |

Only control files are included. No `.VOB` payloads or `.BUP` backups are committed.

## Blu-ray and UHD Blu-ray

| Fixture directory | Files | Coverage |
|---|---|---|
| `BDMV/BD-A` | `index.bdmv`, `MovieObject.bdmv` | BDMV 0200 / HDMV structures |
| `BDMV/BD-B` | `index.bdmv`, `MovieObject.bdmv` | BDMV 0300 / BD-J structures |
| `CLPI/BD-A` | `00000.clpi` | CLPI 0200 header, timing, and stream parsing |
| `CLPI/UHD-A` | `00589.clpi` | CLPI 0300, HEVC, HDR/color-space, and CPI evidence |
| `CLPI/BD-3D` | `00301.clpi` | MVC dependent-view extension stream |
| `MPLS/BD-A` | `00000.mpls` | MPLS 0200 playlist and stream parsing |
| `MPLS/UHD-A` | `00149.mpls` | MPLS 0300 playlist, HEVC streams, and marks |
| `MPLS/BD-3D` | `00800.mpls` | MVC base/dependent relationship and extension data |
| `MPLS/BD-B` | `00050.mpls` | Short-playlist chapter-mark boundary regression |
| `MPLS/UHD-B` | `00800.mpls`, `00801.mpls` | Long ordered segment maps, chapter sentinel, and unsupported extension evidence |

All retained Blu-ray files are navigation metadata. No `.m2ts` or `.ssif` payloads,
decrypted content, AACS keys, title keys, volume keys, or secrets are included.
CLPI and MPLS samples are selected for specific parser and mapper assertions rather
than bulk parse-only coverage. Repeated clip references in long-playlist tests use
one representative CLPI sample with synthetic file-size metadata.

## Integrity and maintenance

`TheDiscDb.OpticalDiscParsers.Tests/fixtures/MANIFEST.sha256` records SHA-256 hashes
for every retained fixture. Keep the manifest synchronized when files are added,
removed, or renamed. Tests should target the behavior a fixture demonstrates;
adding more files from the same source disc is not a substitute for distinct
regression assertions.

New fixtures require documented redistribution permission before being committed.
Prefer vendor-provided, public-domain, TheDiscDb-owned, or explicitly authorized
control-file samples, and use neutral directory names.
