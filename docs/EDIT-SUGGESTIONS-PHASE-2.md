# Edit Suggestions: Phase 2

## Overview

Phase 1 delivered the core "suggest an edit" experience: signed-in users can
propose changes to existing records, those changes are captured as a
snapshot-diff edit suggestion, and admins review them in `/admin/changes`.

Phase 2 broadens what can be suggested (beyond editing existing fields), fills
in metadata gaps, and matures the admin review-to-publish workflow. This
document is a backlog of candidate work — nothing here is committed or
scheduled. Pick items off it as priorities allow.

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
- **Messaging** (`EditSuggestionMessage` / `SendMessage`), **history/audit trail**
  (`EditSuggestionHistoryService`), and **email notifications**
  (`EditSuggestionNotificationService`, still gated behind the global
  `EditSuggestions:Notifications:Enabled` switch).
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
- UPC / ASIN / identifiers

**Approach**: extend the existing snapshot-diff suggestion model and the
`ReleaseEdit` page rather than introducing a new pipeline. Watch out for
list-valued fields (cast, categories) — diffing collections is more involved
than scalar fields, and must avoid storing unstable database `int` IDs.

### 2. Add / remove discs and disc-items

> **Status: Partial.** Add/remove ships for **disc-items** (`disc-item.add` /
> `disc-item.delete`), **tracks** (`track.add` / `track.delete`), and **chapters**
> (`chapter.add` / `chapter.delete`). **Still pending:** adding/removing a whole
> **disc** on a release (no `disc.add` / `disc.delete` change type yet).

Phase 1 mostly edits records that already exist. Phase 2 should let users
suggest **structural** changes:

- Add a disc to a release / remove a disc
- Add or remove a disc-item (title) on a disc

**Approach**: model add/remove as first-class suggestion operations (not just
field diffs). The admin review UI needs to render "added" / "removed" entities
clearly, and the publish step must create/delete records in the `data` repo.

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
> commit/PR to `data/` (vs. the external tool), and **bulk approve/reject across
> multiple suggestions** (today's approve-all is scoped to one suggestion).

Today admins review suggestions; phase 2 should close the loop to the `data`
repo:

- One-click **approve → commit to the `data` repo** (branch/PR or direct
  commit, TBD)
- Rich **diff preview** in the admin detail page (field-level before/after)
- **Bulk approve / reject** for batches of related suggestions

**Approach**: this is the highest-leverage item — it turns suggestions from a
queue admins manually transcribe into an automated publish flow. Decide the
git strategy (PR vs. direct commit) and how approvals map to commits/authors.

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

> **Status: Pending.** No expected-disc-count, missing-disc placeholder, or
> "partial / help wanted" surfacing exists yet. Pairs with whole-disc add/remove
> from #2.

Let a release represent the case where the database has **only some** of the
discs from a known multi-disc set (e.g., a 4-disc boxset where we've documented
2 discs). This surfaces exactly where the database needs help and tells users
which specific discs are worth contributing.

Pairs naturally with **add/remove discs (#2)**: marking a release as partial is
the signal that invites the "add this missing disc" contribution flow.

**Approach**:

- Represent the expected disc count / known-missing discs at the release level
  (e.g., a known total vs. documented discs, or explicit placeholders for
  missing discs). Decide whether a "missing disc" is a real placeholder record
  or just a count delta.
- Surface a **"partial / help wanted"** indicator on the release detail page and
  in listings, ideally calling out which disc numbers/slots are missing.
- Add a **"contribute this disc"** entry point on each missing slot that feeds
  the add-disc flow from #2.
- Consider a browseable view of partial releases ("releases needing discs") so
  contributors can find gaps across the database.

**Open questions**: how is "expected total discs" known/entered (manual vs.
metadata source)? Is a missing disc a placeholder entity or a derived gap? How
does a partial release render in search/SEO without looking like broken data?

### 7. Per-user notification preferences

> **Status: Pending.** Notifications ship (global-config gated) and emails carry a
> static `List-Unsubscribe` header, but there is **no per-user preferences
> store, UI, or gate**, and no wired one-click unsubscribe endpoint.

Phase 1 ships edit-suggestion email notifications fully wired but **dormant**
behind a single global config switch (`EditSuggestions:Notifications:Enabled`),
with one summary email per suggestion at final resolution. Once notifications are
turned on more broadly, individual users should be able to opt out of some or all
of them.

**Approach**:

- Add a user settings/preferences area where a user can toggle notification
  categories (e.g., submission confirmations, resolution emails, message emails).
- Persist preferences per user (new table or columns on the user/profile record).
- Each `Notify*` call first checks the recipient's preferences and skips sending
  if opted out — a **per-user gate layered on top of the global config switch**.
  The existing `IEditSuggestionRecipientResolver` is the natural place to also
  surface a recipient's notification preferences.
- Honor the existing `List-Unsubscribe` email headers by wiring a one-click
  unsubscribe endpoint to these preferences.
- Consider unifying with contribution-email preferences so users manage all
  TheDiscDb email in one place.

**Open questions**: opt-out vs. opt-in defaults per category? Should
transactional/critical messages (e.g., "your suggestion was rejected") be
exempt from opt-out? One preferences surface for all email types, or per-feature?

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

> **Status: Pending.** The enum still exists (`Web`, `GraphQL`, `ApiKey`) with only
> `Web` ever written; the keep-vs-remove decision below has not been made.

`EditSuggestionChange` carries an `EditSuggestionSource` (`Web`, `GraphQL`,
`ApiKey`). Today every suggestion comes from the web UI, so only `Web` is ever
written and the other values are speculative. **Decide whether this field earns
its place**:

- **Remove it** if we don't foresee non-web submission paths, and reintroduce it
  later if/when a GraphQL mutation or API-key ingestion path is actually built.
- **Keep and extend it** if we want a single, uniform way to record provenance —
  in which case also consider tracking other change origins the same way (e.g.
  the existing contributions pipeline) so source attribution is consistent across
  the system rather than edit-suggestion-only.

Captured from PR review feedback; no action taken in phase 1 beyond this note.

## Suggested Sequencing

A reasonable order, roughly by leverage and dependency:

1. **Admin approve → publish automation (#4)** — makes every other suggestion
   type actually reach production; highest payoff.
2. **Release-level metadata editing (#1)** — extends the existing pattern,
   relatively contained.
3. **Add / remove discs and disc-items (#2)** — needs the structural-change
   model and benefits from #4 being in place.
4. **Partial releases (#6)** — builds directly on #2; turns "missing disc" into a
   visible, contributable gap.
5. **Image suggestions (#3)** — independent; can slot in when image UX is a
   priority.
6. **Non-movie media parity (#5)** — an audit pass that can run alongside the
   others.

## Open Questions

- Publish strategy for approved suggestions: open a PR against `data`, or
  commit directly? Who is the commit author/attribution?
- How are collection-valued fields (cast, categories) diffed and merged without
  relying on unstable `int` IDs?
- For add/remove operations, how do we represent a not-yet-existing entity in a
  snapshot diff?

> These workstreams were captured from a brainstorming discussion. Treat this as
> a living backlog — refine, re-prioritize, or split into tracked issues when a
> phase-2 effort actually kicks off.
