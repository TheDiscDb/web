# Global Disc IDs — AACS and DVD identifiers alongside the content hash

> **Status:** 🟡 Core shipped / follow-on backlog (source reviewed 2026-10-29).
> Browser computation, capture, per-pressing persistence, backfill, conflict review,
> data sync, and REST lookup are implemented for Blu-ray/UHD **and DVD**.
> Public campaign browsing and the ecosystem candidates below remain backlog.

## Overview

The optional **Global Disc ID** identifies a physical pressing alongside the
existing content hash. It is the 40-hex AACS Disc ID for Blu-ray/UHD or the 32-hex
libdvdread-compatible DVD Disc ID. Capture and backfill mechanisms are shipped;
complete database coverage is an ongoing contribution goal, not an implementation
milestone established by this document.

Origin: [TheDiscDb/data discussion #388](https://github.com/orgs/TheDiscDb/discussions/388), where
@deekthesqueak proposed adopting the libbluray/`bd_info` Disc ID (the same 40-hex ID used in
`keydb.cfg`, e.g. Watchmen `4D60474073344DD856B68E2195359E8F3E9DAD21`) as an optional field on
`disc##.json`.

## Delivery summary

| Phase | Current state |
|-------|---------------|
| 0. Computation | Shipped: `AacsDiscId`, `DvdDiscId`, and focused tests. Historical manual comparison with an external tool is not re-verified here. |
| 1. Browser | Shipped in the contribution scanner and Add Disc ID page, not just a development PoC. |
| 2. Capture/persist | Shipped: `GlobalDiscId` on contribution/file models and `ReleaseDisc`, import, `.ref` overrides, and file sync. |
| 3. Backfill | Shipped: web add-only attachment, detail-page CTA, conflict review, ContributionBuddy `discid`, `--stats`, and `--resolve-conflicts`. A public missing-ID browse/progress campaign remains proposed. |
| 4. Ecosystem | REST lookup and DVD support ship. Keydb cross-referencing, manual-match fallback, and identifier-based canonical dedup remain proposed. |

Implementation evidence:
- [AACS computation](../code/TheDiscDb.Core/DiscHash/AacsDiscId.cs),
  [DVD computation](../code/TheDiscDb.Core/DiscHash/DvdDiscId.cs), and
  [browser scanner](../code/TheDiscDb.Client/Pages/Contribute/DiscScanner.cs).
- [Pressing model and effective-ID fallback](../code/TheDiscDb.Core/InputModels/ReleaseDisc.cs),
  [backfill service](../code/TheDiscDb.Contributions/DiscId/DiscIdBackfillService.cs),
  [combined identifier mutation](../code/TheDiscDb/GraphQL/Contribute/Mutations/AttachDiscIdentifiers.cs),
  and [web backfill page](../code/TheDiscDb.Client/Pages/Contribute/AddDiscId.razor.cs).
- [REST endpoints](../code/TheDiscDb/Endpoints/DiscLookupEndpoints.cs) and
  [ContributionBuddy backfill/statistics](https://github.com/TheDiscDb/tools/blob/main/tools/ContributionBuddy/DiscIdBackfillTask.cs).

## Research findings (verified)

- **Algorithm**: `AACS Disc ID = SHA1(bytes of AACS/Unit_Key_RO.inf)` → 40-hex, no `0x` prefix.
  Verified against `freemkv/libfreemkv` (`src/disc/mod.rs`, `src/aacs/keys.rs`), the OVID project,
  and MakeMKV/ArchWiki community notes. The Disc ID is **distinct from the decryption key (VUK)** —
  computing it needs **no AACS keys**, only the *unencrypted* `Unit_Key_RO.inf` file bytes.
- **Formats**: Blu-ray **and UHD/4K** use the identical rule. DVD support is also
  implemented: `DvdDiscId` computes uppercase MD5 over `VIDEO_TS.IFO` followed by
  the applicable title-set IFO bytes, with libdvdread's 10-file cap.
- **Input is already reachable**: the shared directory picker supplies disc-root
  files to `DiscScanner`. It reads `AACS/Unit_Key_RO.inf` (or its `DUPLICATE`
  fallback) for BD/UHD and IFO bytes for DVD. Missing/unreadable optional ID
  metadata does not prevent the existing content-hash flow.
- **Where hashing runs today**: the current MD5 **size-hash** is computed **server-side** in the
  `HashDisc` GraphQL mutation (`files.OrderBy(Name).CalculateHash()` in
  `TheDiscDb.Core/DiscHash/HashingExtensions.cs`, MD5 over each file's `Size`).
  The AACS/DVD Disc ID is computed **client-side** in WASM and only the resulting
  40- or 32-hex string is submitted on the identifier-capture path. The current flow also sends file
  inventory metadata for a separate SHA-256 fingerprint; that fingerprint is not
  the AACS/DVD Disc ID and does not replace the legacy canonical content-hash key.
- **Add-only precedent**: `ContentHash` is already treated as **add-only** in
  `DiscFieldsUpdate.cs` (may be filled for a disc that has none; existing value immutable). The
  Disc ID field follows the same add-only rule.
- **Availability**: present on MakeMKV **folder backups** and mounted discs; **absent on direct
  MKV-only rips** → the Disc ID is an **optional** field, captured opportunistically.

## Backfill mechanism (confirmed)

When a user points at their disc/backup, the flow computes the existing
**content hash** (to match the DB record), the optional **AACS/DVD Disc ID**,
and the inventory fingerprint. A clean identifier add updates the database and
records an applied edit-suggestion change for batch sync to `/data`. Conflicts
are filed for review instead of overwriting existing values.

## Phases

### Phase 0 — Computation spike ("can we compute it?")

**Implemented:** reusable `AacsDiscId.Compute` and `DvdDiscId.Compute`, with
dedicated unit tests. The original proposed physical-backup comparison against
`bd_info`/keydb is a manual validation step; this source review does not certify
that a particular corpus/spike report was recorded.

### Phase 1 — Browser proof-of-concept ("in the browser?")

**Implemented in the normal flow:** `DiscScanner.ScanAsync` computes the optional
ID in the browser, and `AddDisc` / `AddDiscId` use the result. A standalone
development-only proof-of-concept is no longer a prerequisite.

### Phase 2 — Capture + persist for NEW contributions

**Implemented:**
- **Field name:** `GlobalDiscId`, not `AacsDiscId`; it covers both ID formats.
- **Storage:** persisted on `ReleaseDisc`, not the shared canonical `Disc`.
  `Disc.GlobalDiscId` is a nonmapped import/file carrier. The field also flows
  through `DiscFile` and `UserContributionDisc`, with migrations/indexes.
- **Capture:** `DiscScanner` computes the ID and contribution creation/update stores it.
- **Import/sync:** disc JSON uses `GlobalDiscId`; a reference can override it with
  `globalDiscId` on `disc*.ref`. Import, contribution generation, and the DB-first
  edit-sync rail preserve the pressing-local value.

### Phase 3 — Backfill campaign for EXISTING discs

- **Shipped flow**: user picks disc/backup → compute content-hash + Disc ID → match
  the existing disc → file an **add-only** change attaching the ID. The current web
  page also submits the inventory fingerprint through `AttachDiscIdentifiers`.
- **Shipped surfacing/progress**: disc detail displays the effective ID and a
  "help add it" entry point. ContributionBuddy `discid --stats` reports coverage
  overall/by format, including unambiguous shared-pressing fallback. Its interactive
  backfill command supports release selection and conflict resolution.
- **Remaining campaign UX**: a public browseable list of missing IDs and campaign
  progress. Optional achievement/leaderboard integration belongs with the badges plan.
- **Guardrails**: add-only per pressing (never overwrite an existing id). The Disc ID lives on the
  **`ReleaseDisc`** (the pressing), so two releases whose discs share a content hash each carry
  their own id and never fight over one slot. A submission is a **conflict** (filed as a pending
  change for admin review, DB untouched) when the id already belongs to a *different* release-disc,
  or the target release-disc already resolves to a *different* id. When the release is known
  (contribute flow / disc-detail CTA) the id is attributed to that release; a hash-only match with
  no release context defaults to the primary/canonical `disc.json`'s release.
- **Shared pressings & read-time fallback**: the same physical pressing is often sold in several
  products (a standalone release + a boxset that references it via `.ref`). Those share one content
  hash → one canonical `Disc` → the **same** Disc ID, but the id is **stored once** (on the
  release-disc whose `disc.json` records it; a `.ref` only carries its own `globalDiscId` when it is
  a *different* pressing/re-press). Read paths **derive** the id: a release-disc's *effective* Disc
  ID is its own stored value, else the single distinct id shared by the other release-discs of the
  same canonical disc (`ReleaseDiscExtensions.EffectiveGlobalDiscId`), else none when siblings
  disagree (a re-press collision). This drives display (the GraphQL `globalDiscId` output field, the
  disc-detail id + "help add it" CTA), CLI coverage stats, and the backfill "already recorded" check —
  so a shared-pressing sibling counts as filled without duplicating the id or tripping the unique
  index.

### Phase 4 — Ecosystem (partially shipped)

- **Lookup by Disc ID** → disc + item/mpls mapping (the #388 use cases: submit disc details
  without a release, MakeMKV/Jellyfin naming, re-extraction lookup).
  - **REST** — shipped: anonymous `GET /api/discid/{globalDiscId}` (`DiscLookupEndpoints`).
    Returns an **array** of every release that carries the id — the storing release-disc plus any
    shared-pressing siblings (release-discs of the same canonical disc that store no id), excluding
    re-presses that store a different id. So a query for one id finds *all* products containing that
    pressing. The traversal is two indexed seeks (unique id index → FK `DiscId` index), tiny
    cardinality — no scans.
  - **GraphQL** — a dedicated `discsByGlobalDiscId` resolver was **cut** (not deferred): the
    built-in HotChocolate filter already exposes the stored `globalDiscId` (`ReleaseDiscFilterType`),
    and the root `mediaItems` / `boxsets` queries reach it through `releases → discs`. Clients use
    the built-in nested filter instead of a bespoke resolver, e.g.:

    ```graphql
    query {
      mediaItems(where: { releases: { some: { discs: { some: {
        globalDiscId: { eq: "40DB92A566F17D96E053579521D676C50800F682" } } } } } }) {
        nodes { title slug }
      }
    }
    ```

    Note the **filter** matches the *stored* id only (the release-disc that records it), whereas the
    `globalDiscId` **output** returns the effective value (a shared-pressing sibling shows the id
    even though it stores none) — so a disc's output `globalDiscId` can be non-null yet not be
    matched by an equality filter on it. Use the REST endpoint when you need every release carrying a
    shared id.


    (`boxsets` supports the same nested path.) `ReleaseDisc.globalDiscId` and the REST response are
    scalar (one pressing = one id).
- `keydb.cfg` / fvonline-db cross-referencing (same Disc ID).
- **DVD** `DVDDiscID` computation/capture is shipped, not deferred investigation.
- General disc-without-release **staging** remains a separate candidate.
  [Rapid Intake](FEATURE-RAPID-DISC-INTAKE.md) already stages administrator-captured
  releases/discs; that is not a public release-less contribution flow.
- Consider Disc ID as a **canonical dedup key** (overlaps Disc Dedup future work).

## Open questions

- **Resolved field naming:** `GlobalDiscId` carries the 40-hex AACS or 32-hex DVD ID.
- ~~Conflict policy when content-hash matches but Disc IDs differ (re-press variants) — review vs
  reject vs allow-multiple.~~ **Resolved: the Disc ID is a property of the pressing, stored as a
  scalar `ReleaseDisc.GlobalDiscId`** (globally unique across release-discs). A canonical `Disc`
  (keyed by `Format` + size-based `ContentHash`) represents shared *content*; distinct pressings
  that share it are separate `ReleaseDisc` rows (a full `disc.json` and one or more `.ref`s), each
  carrying its own id. A different id for the same content is attributed to the pressing/release it
  belongs to (via contribute/CTA context, else the primary `disc.json`); a genuine collision (id on
  a different pressing, or a different id on the same pressing) is filed as a conflicted change for
  review.
- Whether the Disc ID eventually augments/replaces the size-hash as the dedup/canonical key.
- Manual-search fallback for backfill when no content-hash match exists (deferred for now).

## Risks / considerations

- **Availability gaps**: MKV-only rips lack the file → optional field; backfill depends on users who
  kept backups or still own the disc.
- **Match reliability**: backfill matches on the size-based content-hash; if that hash is weak or
  collision-prone, mis-matches could attach a Disc ID to the wrong disc → consider requiring the
  submitted content-hash to agree before accepting.
- **Legal/privacy**: the AACS file's SHA-1 identifier is distinct from the VUK.
  The identifier-capture flow persists only the ID (or the DVD IFO-derived MD5),
  not decryption keys. Inventory fingerprints are separate metadata identifiers.
- **Auditing**: audit Disc ID add/backfill changes (who, disc, value, source), especially given
  add-only semantics and conflict cases.
