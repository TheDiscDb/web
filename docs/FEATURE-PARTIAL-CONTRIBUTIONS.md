# Feature Spec: Partial Contributions

## 1. Summary

Let users contribute **part** of a release/disc and mark what's still missing, so other
contributors can find and complete it. A "partial" designation must **persist in the `/data`
repo** (beyond the transient contribution stage), surface on **release and disc detail pages**,
and feed a **partial-data hub** listing everything that could use contribution to become complete.

Partial state is primarily a **contributor declaration** (the system usually can't infer it),
captured with a **canned reason + optional free-text note**. One case is auto-detected as a
backstop: discs that arrive with **zero identified items** (notably every **Engram**/API
submission) are auto-flagged as "needs identification."

Marker changes follow the existing edit-suggestion **sync pattern**: the database is updated
immediately for live display, and an *applied-but-unsynced* change is filed for the batch
file-sync tool (ContributionBuddy) to write back into `/data` via a PR.

## 2. Status

🔵 **Proposed** — no implementation started.

| Phase | Scope |
|-------|-------|
| **Phase 1** | Declaration + persistence: `partial { reason, note }` on `disc*.json` and `release.json`/`boxset.json`; DB columns; import reads them; contributor declaration UI (incl. the "I don't intend to identify this disc" escape hatch); auto-flag zero-item discs; **badges on release + disc detail pages**; the DB-immediate / applied-but-unsynced **sync rail**. |
| **Phase 2** | **Partial-data hub** (browse all partial releases/discs, filterable by type/reason) + **resolution flows**: add-missing-disc (retargeted disc-contribution) and marker **clearing** (auto-clear on resolution + manual clear). |
| **Phase 3** | Analytics & notifications (e.g. notify the original contributor when a disc they left partial is completed; hub prioritization/insights). |

## 3. Background: how the data & pipeline work today

- **`/data` layout** (per release folder): `metadata.json` (movie/series), `release.json`
  (release), `boxset.json` (boxset + `BoxSetDisc` list), `disc*.json` (a disc's identified items,
  bucketed into `MainMovies`/`Episodes`/`Extras`/…), `disc*.txt` (MakeMKV log),
  `disc*-summary.txt`, and `disc*.ref` (dedup pointer to a canonical disc in another release).
- **Canonical discs**: identical disc content is stored once and shared via `disc*.ref` /
  `ReleaseDisc`. A disc's *identification state* is therefore a property of the shared disc, not
  of one release.
- **Interactive identification gate**: `IdentifyDiscItems.razor` disables **Done Identifying**
  when `identifiedTitles.Count == 0` — so a disc can't currently be finished with no items.
- **Engram** (`/api/engram`): an API ingestion path (submit disc by content hash, upload scan
  logs, upload images) with **no interactive identification** — every Engram disc lands with logs
  and **zero identified items** by construction.
- **Edit-suggestion sync rail** (`IEditSuggestionSyncService`): approved changes are applied to
  the DB (`Status = Applied`, `SyncedToFilesAt == null`); the batch tool reads unsynced changes,
  opens a `/data` PR, then calls `MarkSyncedAsync`. Edit suggestions already handle editing disc
  items; their Phase 2 backlog lists **"whole-disc add/remove"** as pending — **this feature owns
  that**.

## 4. Taxonomy of partial data

| Level | Type | How it's set | Resolved by |
|-------|------|--------------|-------------|
| Release / boxset | **Missing disc(s)** — combo missing a format, boxset missing discs, series missing discs | **Declared** (can't infer a disc that never arrived) | Add-missing-disc flow (§5.5) |
| Disc | **Partially identified** — main feature done, extras/episodes still pending | **Declared** by the contributor (the count of items that *should* be identified isn't inferable — a disc may have many titles where only the main feature ever should be identified) | Existing suggested-edit flow |
| Disc | **Empty / unidentified** — logs only, zero items | **Declared intent** ("I don't intend to identify this disc," with reason) **or** **auto/pending** (Engram/API/import discs auto-flagged "needs identification") | Existing suggested-edit flow |

Out of scope for this feature (noted only): missing release **metadata/assets** (cover image,
UPC/ASIN, etc.) — a gap, but not a disc gap.

## 5. Design

### 5.1 Reason designation
Every declared partial carries a **context-specific canned reason code** plus an **optional
free-text note**, with an **"Other"** code (free-text expected). Canned codes drive hub filtering
and pick the right resolution flow; free-text captures specifics. Starter lists (finalize during
build):

- **Missing disc**: `OnlyOwnThisFormat`, `MissingDisc`, `DiscDamagedOrUnreadable`, `Other`.
- **Partial / empty disc**: `OnlyIdentifyingMainFeature`, `ExtrasOutOfScope`,
  `LogsOnlyForOthersToComplete`, `Other`.

### 5.2 Persistence in `/data`
- **Disc-level** — add an optional object to `disc*.json`:
  ```json
  "partial": { "reason": "OnlyIdentifyingMainFeature", "note": "12 extras still need doing" }
  ```
  Because discs dedup via `disc*.ref`, a disc-level marker is naturally shared by every release
  referencing that canonical disc — consistent with the canonical model.
- **Release / boxset-level (missing discs)** — add a **release-level marker** to
  `release.json` (and `boxset.json`): a single `partial { reason, note }` where `note` is
  free-text describing what's missing. **No structured per-disc "slots."**

### 5.3 Database & import
- Import reads the `partial` objects and materializes queryable state so the hub and badges can
  filter without reparsing files:
  - Disc-level partial → on the **canonical `Disc`** (shared identification state).
  - Release-level partial → on **`Release`** (and boxset as applicable).
- **Open question**: whether the disc-level marker attaches to `Disc` vs `ReleaseDisc` given
  canonical sharing — confirm at implementation (§8).

### 5.4 Declaration UX
- **Identification page**: add an **"I don't intend to identify this disc"** action that bypasses
  the `identifiedTitles.Count == 0` **Done-Identifying** gate and records a reason. Also allow a
  contributor who identified *some* items to mark the disc **partially identified** (reason +
  note) on completion.
- **Release**: allow declaring the release **missing disc(s)** (reason + free-text note).
- **Auto-flag**: any disc that lands with **zero identified items** through a non-interactive path
  (Engram, other API/import) is auto-flagged **empty/pending — needs identification** (distinct
  from a deliberate "don't intend to identify" declaration).

### 5.5 Resolution flows
- **Missing disc → retargeted disc-contribution**: reuse the normal disc-contribution pipeline
  (submit disc logs/images → identify), but **attach to the existing release** instead of creating
  a new one; it flows through the normal contribution **review/import**. (Supersedes the edit-
  suggestion "whole-disc add/remove" backlog item.)
- **Partial / empty disc → existing suggested-edit flow**: contributors already can add/edit disc
  items via edit suggestions; approving those fills in the identification.

### 5.6 Lifecycle & sync
- **Auto-clear where possible + manual clear otherwise**: adding the missing disc clears the
  release marker; identifying the outstanding items (via an approved edit / retargeted
  contribution) clears the disc marker; admins/contributors can also **clear manually**.
- **Sync pattern** (mirror `IEditSuggestionSyncService`): setting or clearing a marker updates the
  **DB immediately** (live site reflection) and files an **applied-but-unsynced** change; the
  batch tool (ContributionBuddy) writes the `partial` object into `/data` via a PR and marks it
  synced on merge.

### 5.7 Surfacing
- **Release detail** and **disc detail** pages show a **partial badge** with the reason (and note
  on hover/expand), plus a call-to-action linking to the appropriate resolution flow.
- **Partial-data hub** (Phase 2): a browse page listing all partial releases and discs, filterable
  by **type** (missing disc / partial disc / empty disc) and **reason**, so contributors can pick
  work to complete.

## 6. Behavior & rules

- Partial is **declared** except the zero-item **auto-flag** backstop.
- Auto/pending (Engram) empty discs are visually distinct from deliberate "don't intend to
  identify" discs.
- Disc-level markers are **shared** across releases that reference the same canonical disc.
- Markers persist until **resolved** (auto-clear) or **manually cleared** — declared partials
  never self-clear on their own.
- All marker writes go **DB-first**, then batch-synced to `/data`; the `/data` repo remains the
  source of truth.

## 7. Risks / considerations

- **Canonical-disc propagation**: a disc-level marker shows on *every* release sharing that disc.
  This is correct (same content, same identification state) but must be understood by reviewers.
- **Staleness**: declared markers rely on clearing; auto-clear-on-resolution plus manual clear
  mitigate, but a partial that's completed via an unexpected path could linger — provide an easy
  manual clear.
- **Engram inflow volume**: auto-flagging every empty Engram disc could flood the hub — the Phase 2
  hub needs filtering/prioritization (and possibly a separate "unidentified" bucket).
- **Reason-list drift**: `Other` free-text will accumulate; periodically review and promote common
  reasons into canned codes.
- **Auditing** (per project preference to consider auditing on state-changing features): partial
  declarations and clears are state changes worth auditing — see §10.

## 8. Open questions

- DB placement of the disc-level marker: `Disc` (canonical, shared) vs `ReleaseDisc` (per-release).
  Canonical sharing argues for `Disc`; confirm at implementation.
- Boxset missing-disc: store on `boxset.json` vs on the member release's `release.json`.
- Final reason-code taxonomy per context (starter lists in §5.1).
- Badge treatment distinguishing auto/pending empty discs from deliberate "don't intend to
  identify" discs.
- Should completing a partial disc **notify** the original contributor? (Phase 3.)

## 9. Rollout / phases

- **Phase 1** — Data model (`partial { reason, note }` in `disc*.json` + `release.json`/
  `boxset.json`), DB columns + import read, contributor declaration UX (incl. the
  don't-intend-to-identify escape hatch), zero-item auto-flag, release/disc detail-page badges,
  and the DB-immediate / applied-but-unsynced sync rail.
- **Phase 2** — Partial-data hub (filterable) + resolution flows: retargeted add-missing-disc
  contribution and marker clearing (auto + manual).
- **Phase 3** — Analytics and notifications (notify on completion, hub prioritization/insights).

## 10. Future enhancements

- **Auditing** of partial declarations/clears (who, which entity, reason, old→new, source).
- **Notifications**: alert the original contributor (and watchers) when a partial they left gets
  completed — ties into the Badges & Achievements and User Preferences features.
- **Hub prioritization/insights**: most-wanted formats, oldest unfinished discs, "quick wins."
- **Metadata/asset gaps** (cover art, UPC/ASIN) as a sibling partial type.
- Promote frequently-used `Other` free-text reasons into first-class canned codes.
