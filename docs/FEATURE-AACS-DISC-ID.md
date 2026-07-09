# AACS Disc ID — a second, globally-stable disc hash

> **Status:** 🔵 Proposed — research complete, feasibility confirmed; no implementation started.

## Overview

Add the **AACS Disc ID** — a globally-stable, per-pressing disc identifier — as a *second* disc
hash alongside the existing content hash, and ultimately **backfill it across every disc in the
database** via user contributions.

Origin: [TheDiscDb/data discussion #388](https://github.com/orgs/TheDiscDb/discussions/388), where
@deekthesqueak proposed adopting the libbluray/`bd_info` Disc ID (the same 40-hex ID used in
`keydb.cfg`, e.g. Watchmen `4D60474073344DD856B68E2195359E8F3E9DAD21`) as an optional field on
`disc##.json`.

The work is staged deliberately:

1. **Prove we can compute it.**
2. **Prove we can compute it in the browser** (pure C#/WASM, like the current hash flow).
3. Persist + capture it for new contributions.
4. **Backfill campaign** — users compute and contribute the Disc ID for existing discs they own.

## Research findings (verified)

- **Algorithm**: `AACS Disc ID = SHA1(bytes of AACS/Unit_Key_RO.inf)` → 40-hex, no `0x` prefix.
  Verified against `freemkv/libfreemkv` (`src/disc/mod.rs`, `src/aacs/keys.rs`), the OVID project,
  and MakeMKV/ArchWiki community notes. The Disc ID is **distinct from the decryption key (VUK)** —
  computing it needs **no AACS keys**, only the *unencrypted* `Unit_Key_RO.inf` file bytes.
- **Formats**: Blu-ray **and UHD/4K** use the identical rule. **DVDs differ** — `DVDDiscID`
  (lsdvd) is computed over the DVD IFO structures; a separate, harder algorithm → **deferred**.
- **Input is already reachable**: `AddDisc.razor.cs` uses the browser **File System Access API**
  to pick the disc **root** folder and reads `.m2ts` sizes under `BDMV/STREAM`. The **`AACS/`
  directory is a sibling** in that same directory handle, so `AACS/Unit_Key_RO.inf` is readable
  with the handle we already hold — **no new user step, no external tool** (satisfies the "nothing
  but MakeMKV" constraint).
- **Where hashing runs today**: the current MD5 **size-hash** is computed **server-side** in the
  `HashDisc` GraphQL mutation (`files.OrderBy(Name).CalculateHash()` in
  `TheDiscDb.Core/DiscHash/HashingExtensions.cs`, MD5 over each file's `Size`). The new Disc ID can
  be computed **client-side** in WASM (`System.Security.Cryptography.SHA1`) and only the 40-hex
  string sent — the file bytes never leave the browser.
- **Add-only precedent**: `ContentHash` is already treated as **add-only** in
  `DiscFieldsUpdate.cs` (may be filled for a disc that has none; existing value immutable). The
  Disc ID field follows the same add-only rule.
- **Availability**: present on MakeMKV **folder backups** and mounted discs; **absent on direct
  MKV-only rips** → the Disc ID is an **optional** field, captured opportunistically.

## Backfill mechanism (confirmed)

When a user points at their disc/backup we compute **both** hashes: the existing **content-hash**
(to *match* their disc to the DB record) **and** the new **AACS Disc ID**. On a content-hash
match, file an **add-only change** attaching the Disc ID to that disc (edit-suggestion-style,
DB-first then batch-synced to `/data`).

## Phases

### Phase 0 — Computation spike ("can we compute it?")

- Add a pure-C# helper in `TheDiscDb.Core/DiscHash` (e.g. `AacsDiscId.Compute(byte[]/Stream)` →
  uppercase SHA1 hex).
- Unit test with a synthetic vector (mirror libfreemkv's `test_disc_hash`).
- **Validation**: maintainer points the helper at a real MakeMKV backup's
  `AACS/Unit_Key_RO.inf` and confirms the output equals `bd_info`'s "Disc ID" / the `keydb.cfg`
  value. This also verifies MakeMKV backups actually retain the file.
- Exit: a trusted, reusable compute function.

### Phase 1 — Browser proof-of-concept ("in the browser?")

- Minimal Blazor WASM dev page: pick folder → locate sibling `AACS/Unit_Key_RO.inf` via File
  System Access API → read bytes → **client-side** SHA1 → display the Disc ID.
- Exit: proof that pure-managed SHA1 runs in WASM and the AACS folder is reachable via the
  existing handle.

### Phase 2 — Capture + persist for NEW contributions

- **Data model**: add optional `AacsDiscId` to `DiscFile` (`disc*.json`), DB `Disc` (+ passthrough
  on `ReleaseDisc`), `UserContributionDisc`, and the relevant InputModels; EF migration + index.
- **Capture**: extend `AddDisc.TryCalculateHash` — when the `AACS` folder is present, compute the
  Disc ID client-side and include it with the disc submission.
- **Persist/sync**: flow through import; DB-first + the batch `/data` **sync rail** (tool writes
  `AacsDiscId` into `disc*.json`), mirroring the edit-suggestion sync pattern.

### Phase 3 — Backfill campaign for EXISTING discs

- **Contribute-Disc-ID flow**: user picks disc/backup → compute content-hash + Disc ID → match
  content-hash to the existing disc → file an **add-only** change attaching the Disc ID.
- **Surfacing/progress**: show the Disc ID (or a "help add it" CTA) on the disc detail page; a
  backfill view listing/counting discs still missing a Disc ID; a progress metric. Optional tie-in
  to Badges/leaderboard to drive participation.
- **Guardrails**: add-only (never overwrite); if the content-hash matches but a *different* Disc ID
  is submitted, flag for review (possible re-press/variant) rather than silently accepting.

### Phase 4 — Ecosystem (future)

- **Lookup by Disc ID** → disc + item/mpls mapping (the #388 use cases: submit disc details
  without a release, MakeMKV/Jellyfin naming, re-extraction lookup).
  - **REST** — shipped: anonymous `GET /api/disc-id/{globalDiscId}` (`DiscLookupEndpoints`)
    returns the disc's titles and their item mapping.
  - **GraphQL** — a dedicated `discsByGlobalDiscId` resolver was **cut** (not deferred): the
    built-in HotChocolate filter already exposes `globalDiscId` (`ReleaseDiscFilterType`), and
    the root `mediaItems` / `boxsets` queries reach it through `releases → discs`. Clients use
    the built-in nested filter instead of a bespoke resolver, e.g.:

    ```graphql
    query {
      mediaItems(where: { releases: { some: { discs: { some: {
        globalDiscId: { eq: "40DB92A566F17D96E053579521D676C50800F682" } } } } } }) {
        nodes { title slug }
      }
    }
    ```

    (`boxsets` supports the same nested path.)
- `keydb.cfg` / fvonline-db cross-referencing (same Disc ID).
- **DVD** `DVDDiscID` investigation (separate algorithm).
- Disc-without-release **staging** (overlaps Partial Contributions / Engram).
- Consider Disc ID as a **canonical dedup key** (overlaps Disc Dedup future work).

## Open questions

- Field naming: `AacsDiscId` (BD/UHD-specific) vs a generic `DiscId` + `DiscIdType` (room for DVD).
- Conflict policy when content-hash matches but Disc IDs differ (re-press variants) — review vs
  reject vs allow-multiple.
- Whether the Disc ID eventually augments/replaces the size-hash as the dedup/canonical key.
- Manual-search fallback for backfill when no content-hash match exists (deferred for now).

## Risks / considerations

- **Availability gaps**: MKV-only rips lack the file → optional field; backfill depends on users who
  kept backups or still own the disc.
- **Match reliability**: backfill matches on the size-based content-hash; if that hash is weak or
  collision-prone, mis-matches could attach a Disc ID to the wrong disc → consider requiring the
  submitted content-hash to agree before accepting.
- **Legal/privacy**: `Unit_Key_RO.inf` is **not** a decryption key (the VUK is separate and not
  stored); we persist only a SHA1 identifier. Call this out explicitly.
- **Auditing**: audit Disc ID add/backfill changes (who, disc, value, source), especially given
  add-only semantics and conflict cases.
