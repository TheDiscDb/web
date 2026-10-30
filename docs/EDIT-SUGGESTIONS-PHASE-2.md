# Edit Suggestions: Phase 2

## Overview

Phase 1 delivered the core "suggest an edit" experience: signed-in users can
propose changes to existing records, those changes are captured as a
snapshot-diff edit suggestion, and admins review them in `/admin/changes`.

Phase 2 broadens what can be suggested (beyond editing existing fields), fills
in metadata gaps, and matures the admin review-to-publish workflow. This
document records delivered work and a backlog of remaining candidates; the
remaining items are not a delivery schedule.

> **Source review: 2026-10-29.** Bulk review across suggestions is merged
> ([web #122](https://github.com/TheDiscDb/web/pull/122)); `Source` has active Web
> and GraphQL writers. Per-user preferences and partial-state work are owned by
> their linked plans below, not separate duplicate implementations.

## Recap: What Phase 1 Shipped

- Edit/suggest pages for **release, disc, disc-item, tracks (bulk), and
  chapters**.
- Snapshot-diff edit-suggestion submission pipeline (partial updates without
  storing unstable `int` IDs — see `web/code/.github/copilot-instructions.md`).
- Admin review surface: `/admin/changes` (`EditSuggestions.razor`) plus the
  per-suggestion detail page (`EditSuggestionDetail.razor`).
- UI polish: standardized buttons, icon-only suggest-edit links, shared
  touch-friendly drag-to-reorder (`touch-sortable.js` + `TouchSortable<T>`).

## Progress Since Phase 1

Since this backlog was captured, a substantial amount of the underlying Phase 2
infrastructure has shipped. It is verified in code and the per-workstream status
below reflects it.

- **Change framework** — `IChange` / `ChangeBase` / `ChangeFactory` /
  `ChangeBuilder` (`code/TheDiscDb.Data/Changes/`) with snapshot-drift + TOCTOU
  conflict detection, so approvals re-validate against current DB state.
- **Registered change types** — `release.fields.update`, `disc.fields.update`,
  `disc-item.add|delete|fields.update`, `track.add|delete|fields.update`,
  `chapter.add|delete|update`. This delivers structural add/remove for
  disc-items, tracks, and chapters (part of #2) and scalar release-metadata
  editing incl. UPC/ISBN/ASIN (part of #1).
- **Admin review** (`EditSuggestionReviewService`) — per-change approve/reject,
  `ApproveAllPendingAsync` (approve-all within a suggestion), conflict resolution,
  and a bundle-status roll-up. Field-level **JSON diff preview** (`JsonDiff`
  control) in `EditSuggestionDetail.razor`.
- **Bulk review across suggestions** —
  [`EditSuggestionBulkReviewService`](../code/TheDiscDb.Contributions/EditSuggestions/EditSuggestionBulkReviewService.cs)
  and the admin queue support selecting multiple suggestions, approve/reject
  confirmation, required rejection reasons, and per-suggestion results. Processing
  is oldest-first with isolated scopes; unresolved conflicts stay for individual review.
- **Disc identifier previews** - admin review and My Changes preserve existing
  `contentHash`, `globalDiscId`, and `fingerprint` values in the displayed diff
  when a disc-field proposal omits them or supplies null/empty values. These
  add-only fields cannot be cleared by approval. Nonempty proposals remain
  visible for review, including values requiring conflict resolution. This is
  display-only and also covers suggestions submitted before the preview fix.
- **Messaging** (`EditSuggestionMessage` / `SendMessage`), **history/audit trail**
  (`EditSuggestionHistoryService`), and **email notifications**
  (`EditSuggestionNotificationService`, gated by
  `EditSuggestions:Notifications:NotifyAdmins` / `NotifyUsers`). Checked-in
  defaults enable admin mail and disable user mail; production overrides are not
  established by this source review.
- **DB → data-repo sync hooks** (`EditSuggestionSyncService`) — approvals apply to
  the DB and flag `SyncedToFilesAt = null`; `GetUnsyncedChangesAsync` /
  `MarkSyncedAsync` let the external ContributionBuddy tool PR the changes into the
  `data/` repo. (The in-app one-click git publish itself is not built; publishing
  is delegated to that tool.)

> Status legend for the workstreams below: **Done** / **Partial** / **Pending**.

## Candidate Workstreams

### 1. Release-level metadata editing

> **Status: Partial.** Scalar fields ship via `release.fields.update` (Title,
> RegionCode, Locale, Year, ReleaseDate, and the **UPC / ISBN / ASIN**
> identifiers). **Still pending:** the list-valued fields below (cast & crew,
> categories / groups, contributors) — these need collection diffing.

Cover the release fields phase 1 doesn't yet let users suggest changes to:

- Cast & crew
- Categories / groups
- Contributors

UPC / ISBN / ASIN scalar edits are already delivered and are not remaining work.

**Approach**: extend the existing snapshot-diff suggestion model and the
`ReleaseEdit` page rather than introducing a new pipeline. Watch out for
list-valued fields (cast, categories) — diffing collections is more involved
than scalar fields, and must avoid storing unstable database `int` IDs.

### 2. Add / remove discs and disc-items

> **Status: Partial.** Add/remove ships for **disc-items** (`disc-item.add` /
> `disc-item.delete`), **tracks** (`track.add` / `track.delete`), and **chapters**
> (`chapter.add` / `chapter.delete`). **Still pending:** adding/removing a whole
> **disc** on a release (no `disc.add` / `disc.delete` change type yet).
> [Partial Contributions](FEATURE-PARTIAL-CONTRIBUTIONS.md) owns the proposed
> add-missing-disc contribution flow; arbitrary disc removal remains here.

Phase 1 mostly edits records that already exist. Phase 2 should let users
suggest **structural** changes:

- Add a disc to a release / remove a disc
- Add or remove a disc-item (title) on a disc

**Approach**: retain the shipped structural operations for disc-items, tracks,
and chapters. Route missing-disc additions through the retargeted contribution
design in the Partial Contributions plan. Whole-disc removal still needs an
explicit review operation and matching database/data-repo semantics.

### 3. Image suggestions (cover art)

> **Status: Pending.** No `release.image.update` change type exists yet (it is
> referenced aspirationally in a code comment only). Cover art still changes only
> via the contribution upload path.

Let users propose replacing the front/back cover as a reviewed edit, instead of
the current contribution-only upload path.

**Approach**: store the proposed image as part of the suggestion (or a staged
blob), show a before/after preview in the admin detail page, and on approval
move the image into the `data` repo alongside the metadata change. Reuse the
existing `SfUploader` components where possible.

### 4. Admin workflow: approve → publish automation

> **Status: Mostly done.** Shipped: per-change approve/reject, **approve-all**
> within a suggestion (`ApproveAllPendingAsync`), **field-level diff preview**
> (`JsonDiff`), conflict detection/resolution, and DB→data-repo **sync hooks**
> (`EditSuggestionSyncService`; the actual PR to `data/` is produced by the
> external ContributionBuddy tool). **Still pending:** an **in-app** one-click
> commit/PR to `data/` (vs. the external tool). **Bulk approve/reject across
> multiple suggestions is also shipped** through `EditSuggestionBulkReviewService`
> and the `/admin/changes` selection/confirmation UI.

The DB-to-data-repo loop already works through ContributionBuddy `sync-edits`.
The remaining candidate is a one-click **approve → commit/PR** flow inside the
admin UI, rather than running the external tool.

**Approach**: reuse the existing sync/export semantics rather than introducing
a second change format. Decide how in-app actions invoke publishing and map
approvals to commits/authors. Missing in-app publishing does not mean approved
suggestions currently need manual transcription.

### 5. Non-movie media parity (TV / boxsets)

> **Status: Partial / unverified.** The change types identify releases by a
> media-item **or** boxset slug pair, so the model is not movie-only. Whether every
> edit page behaves correctly for TV series and boxsets is still an **audit task**
> that hasn't been signed off.

Phase 1 was validated primarily against movies. Audit the edit/suggest pages
against TV series and boxsets and close any gaps (routing, titles, multi-disc
boxset structure, season/episode context).

**Approach**: walk each edit page (`ReleaseEdit`, `DiscEdit`, `DiscItemEdit`,
`TracksEdit`, `ChapterEdit`) with a boxset and a TV release and fix anything
movie-specific.

### 6. Partial releases (incomplete multi-disc sets)

> **Status: Pending; owned by [Partial Contributions](FEATURE-PARTIAL-CONTRIBUTIONS.md).**
> No published partial/help-wanted markers or hub exist. Intake placeholders are
> staging-only and do not implement this feature.

Let a release represent the case where the database has **only some** of the
discs from a known multi-disc set (e.g., a 4-disc boxset where we've documented
2 discs). This surfaces exactly where the database needs help and tells users
which specific discs are worth contributing.

The Partial Contributions plan supersedes this workstream's original placeholder
brainstorm: use a release-level `partial { reason, note }` declaration, not
structured missing-disc slots, and add missing discs through an owned contribution
targeting the existing release.

Declaration/persistence, detail-page badges, the browseable hub, resolution, and
marker clearing are tracked there. Do not separately implement an expected-count
or placeholder schema from this older backlog.

### 7. Per-user notification preferences

> **Status: Pending.** Notifications ship (global-config gated) and emails carry a
> static `List-Unsubscribe` header, but there is **no per-user preferences
> store, UI, or gate**, and no wired one-click unsubscribe endpoint.

Email notifications are wired with separate audience gates (`NotifyAdmins` and
`NotifyUsers` under `EditSuggestions:Notifications`), with one summary email per
suggestion at final resolution. User mail is disabled in checked-in defaults;
admin mail is enabled. Users still need category/master opt-out and a real
one-click unsubscribe endpoint.

**Owner/design:** [User Preferences](FEATURE-USER-PREFERENCES.md) specifies one
shared store/service for contribution and edit-suggestion user mail, opted-in
defaults, category flags plus a master switch, and tokenized unsubscribe.
Admin mail remains independent. These are no longer separate undecided designs here.

### 8. "My Suggested Changes" page design pass

> **Status: Pending.** `MyChanges.razor` (`/changes/my`) exists and works; the
> deliberate UX/design pass described below has not been done.

The user-facing `My Suggested Changes` list page (`/changes/my`,
`MyChanges.razor`) currently renders a bare QuickGrid (Target / Summary /
Changes count / Submitted / Status / View). It works but **needs a dedicated
design discussion** before further polish — the layout, status presentation,
empty/loading states, and how a user drills into and tracks the lifecycle of
their suggestions all deserve a deliberate UX pass rather than incremental
tweaks.

**Approach**: treat this as a design task, not a code task. Decide the
information hierarchy (group by status? surface admin messages / rejection
reasons inline? show progress through the submit → review → published
lifecycle?), then implement against an agreed mock. Pairs with the breadcrumb /
detail-page work already done on `MyChangeDetail`.

**Open questions**: card vs. table layout? How prominently to surface
admin feedback (notes / rejection reasons)? Should this page and the admin
review surface share components?

### 9. Evaluate the `EditSuggestionSource` enum

> **Status: Original removal rationale superseded.** `Source` is on the
> [`EditSuggestion`](../code/TheDiscDb.Data/Models/EditSuggestion.cs) bundle,
> not `EditSuggestionChange`. Web pages write `Web`, while
> [`DiscIdBackfillService`](../code/TheDiscDb.Contributions/DiscId/DiscIdBackfillService.cs)
> writes `GraphQL` for Disc ID/fingerprint backfills and conflicts.

The enum now records real non-web provenance and is displayed in the admin
queue. Removing it because only Web is used is no longer applicable. `ApiKey`
remains an unused reserved value; broader provenance standardization is an
optional follow-on, not an unresolved blocker for Phase 2.

## Suggested Sequencing

A reasonable order, roughly by leverage and dependency:

1. **In-app publishing convenience (#4)** — optional replacement for invoking
   the working external sync tool; not a prerequisite for publishing other edits.
2. **Release-level metadata editing (#1)** — extends the existing pattern,
   relatively contained.
3. **Whole-disc operations (#2)** — disc-item/track/chapter operations already ship.
4. **Partial releases (#6)** — follow the dedicated plan for declarations and
   retargeted missing-disc contributions.
5. **Image suggestions (#3)** — independent; can slot in when image UX is a
   priority.
6. **Non-movie media parity (#5)** — an audit pass that can run alongside the
   others.

## Open Questions

- In-app publishing integration: how should it invoke the existing external
  branch/PR workflow, and preserve commit author/attribution?
- How are collection-valued fields (cast, categories) diffed and merged without
  relying on unstable `int` IDs?
- For add/remove operations, how do we represent a not-yet-existing entity in a
  snapshot diff?

> These workstreams were captured from a brainstorming discussion. Treat this as
> a living backlog — refine, re-prioritize, or split into tracked issues when a
> phase-2 effort actually kicks off.
