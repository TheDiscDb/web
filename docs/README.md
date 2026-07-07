# Project Plans Index

This folder holds design docs and plans for larger, cross-cutting bodies of work
on TheDiscDb. Use this index to track what is planned, in progress, or captured
for the future across the whole project.

> **Status legend**
> - 🟢 **Shipped** — implemented and merged.
> - 🟡 **In progress / backlog** — partially delivered, or an active backlog of candidate work.
> - 🔵 **Proposed** — captured design/reference; no implementation started.

## Plans

| Plan | Status | Summary |
|------|--------|---------|
| [Image Storage Scaling](IMAGE-STORAGE-SCALING-PLAN.md) | 🔵 Proposed | Make Azure Blob the master store for images, pull image files out of the `data` git repo, and rewrite history to shrink the repo. JSON metadata stays in git. Reference design only — no code implemented. |
| [Canonical Disc Deduplication (Phase 4)](PHASE-4-CANONICAL-DISC-DEDUPLICATION.md) | 🟢 Shipped | Disc storage refactored so identical disc content (tracks + chapters) shared across releases is stored once as a canonical `Disc` and referenced via `ReleaseDisc`, rather than duplicated per release. Core model, rebuild, import/contribution dedup, and `data/` repo `disc*.ref` dedup are all implemented. |
| [Disc Deduplication — Future Work](DISC-DEDUP-FUTURE-WORK.md) | 🔵 Proposed | Deferred follow-ons to the shipped canonical disc model: disc versioning, smart copy detection, moderator merge tooling, automated duplicate detection, analytics/insights, and optional `ReleaseDisc` denormalization. |
| [Edit Suggestions (Phase 2)](EDIT-SUGGESTIONS-PHASE-2.md) | 🟡 In progress / backlog | Phase 1 (suggest/review edits to existing records) shipped. Phase 2 partially shipped: change framework + change types (release scalar fields, add/remove disc-items/tracks/chapters), admin approve/reject/approve-all with diff preview & conflict resolution, messaging, history, notifications, and DB→data-repo sync hooks. Still pending: list-valued release metadata, whole-disc add/remove, image suggestions, partial releases, per-user notification prefs, "My Changes" design pass, and the `EditSuggestionSource` decision. |
| [Badges & Achievements](FEATURE-BADGES-ACHIEVEMENTS.md) | 🔵 Proposed | Gamification layer: contributors earn badges/achievements for milestones and quality contributions, grouped into auto-derived levels, displayed on the `/contributedby/{username}` profile and `/leaderboard`. Meaningful achievements are gated on post-review outcomes (Approved/Imported) so quality isn't sacrificed for volume. Phase 1 = model + backfill + full catalog + levels + display; Phase 2 = dedicated achievements browse/progress page. |
| [Reliable Audio & Subtitle Track Labeling](FEATURE-TRACK-LABELING.md) | 🔵 Proposed | Make `disc*-summary.txt` audio/subtitle labels reliable and add subtitle support. `AudioTrack[n]` keeps its current meaning (backward compatible); `SubtitleTrack[n]` is added; the importer validates every reference against the immutable `disc*.txt` MakeMKV log and hard-fails on mismatch. Phase 0 = audit report quantifying how much legacy hand-authored data is mislabeled; Phase 1 = subtitle labeling end-to-end (model, UI, mutation, serializer, parser, importer). |
| [User Preferences](FEATURE-USER-PREFERENCES.md) | 🔵 Proposed | Let users opt out of some or all emails via a small per-user preferences framework (email prefs are its first consumer). A typed `UserPreferences` row holds a master switch + per-category flags; both notification services consult a shared preference service before any user-facing send (admin email unaffected). Adds tokenized one-click unsubscribe wired into a real `List-Unsubscribe` header (replacing today's `mailto`) plus a settings UI on `Account/Manage`. Opted-in by default; missing row = all on. Phase 1 = table + enforcement + one-click + UI; Phase 2 = announcements/digest + auditing. |
| [Partial Contributions](FEATURE-PARTIAL-CONTRIBUTIONS.md) | 🔵 Proposed | Let users contribute part of a release/disc and mark what's missing so others can complete it: missing discs (combo/boxset/series), partially-identified discs, and empty/unidentified discs (incl. auto-flagged Engram/API submissions). Partial state is a contributor declaration (canned reason + free-text) persisted in `/data` (`partial { reason, note }` on `disc*.json` and `release.json`), surfaced via detail-page badges and a partial-data hub. Marker writes go DB-first then batch-sync to `/data` like edit suggestions. Missing discs resolve via a retargeted disc-contribution; partial discs via suggested edits. Phase 1 = declaration + persistence + auto-flag + badges + sync; Phase 2 = hub + resolution/clearing; Phase 3 = analytics/notifications. |

## Conventions

- One doc per major initiative. Prefer an ALL-CAPS, hyphenated file name that
  describes the initiative (e.g. `IMAGE-STORAGE-SCALING-PLAN.md`).
- Start each plan with an `## Overview` and, where useful, a status note at the top.
- When you add or retire a plan, update the table above so this stays the single
  place to see the overall roadmap.
- When a plan is fully delivered, mark it 🟢 **Shipped** here (optionally move
  detail into code/docs) rather than silently deleting the doc.
