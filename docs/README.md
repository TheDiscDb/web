# Project Plans Index

This folder holds design docs and plans for larger, cross-cutting bodies of work
on TheDiscDb. Use this index to track what is planned, in progress, or captured
for the future across the whole project.

## Source review: 2026-10-29

Reconciled against merged `main` in `web` (`0a08b2c`), `tools` (`acf147b`),
and `data` (`768d2c50f`). This includes the merged dependency/licensing updates
([web #124](https://github.com/TheDiscDb/web/pull/124),
[tools #35](https://github.com/TheDiscDb/tools/pull/35)).
Local-only branches and uncommitted changes are not evidence of delivery.

Statuses describe source implementation, not confirmed production deployment.
Checked-in feature defaults are not proof of live configuration. An audit tool,
test suite, or release script does not establish that its corpus audit, physical-disc
smoke test, or public release has been completed.

> **Status legend**
> - 🟢 **Shipped** — implemented and merged.
> - 🟡 **In progress / backlog** — partially delivered, or an active backlog of candidate work.
> - 🔵 **Proposed** — captured design/reference; no implementation started.

## Plans

| Plan | Status | Summary |
|------|--------|---------|
| [Image Storage Scaling](IMAGE-STORAGE-SCALING-PLAN.md) | 🔵 Proposed | Blob serving exists, but the master-store migration has not shipped. Redirect image generation in both web and tools, add replacement archives, prove blob parity, remove images/rewrite data history, and document read-only development access. |
| [Canonical Disc Deduplication (Phase 4)](PHASE-4-CANONICAL-DISC-DEDUPLICATION.md) | 🟢 Shipped | Disc storage refactored so identical disc content (tracks + chapters) shared across releases is stored once as a canonical `Disc` and referenced via `ReleaseDisc`, rather than duplicated per release. Core model, rebuild, import/contribution dedup, and `data/` repo `disc*.ref` dedup are all implemented. |
| [Disc Deduplication — Future Work](DISC-DEDUP-FUTURE-WORK.md) | 🟡 Partial / backlog | Exact-match contribution copy detection already ships. Remaining candidates: near-match detection, disc versioning, moderator merges, duplicate reports, reuse analytics, and profiling-driven denormalization. |
| [Edit Suggestions (Phase 2)](EDIT-SUGGESTIONS-PHASE-2.md) | 🟡 Partial / backlog | Change framework, scalar release edits, structural disc-item/track/chapter edits, bulk review across suggestions, diff/conflict handling, messaging/history, and external data-repo sync ship. Pending: collection-valued metadata, whole-disc operations, image suggestions, in-app publishing, TV/boxset audit, and My Changes design. Partial-state and preferences work live in their own plans; `Source` has active Web and GraphQL writers. |
| [Badges & Achievements](FEATURE-BADGES-ACHIEVEMENTS.md) | 🔵 Proposed | Gamification layer: contributors earn badges/achievements for milestones and quality contributions, grouped into auto-derived levels, displayed on the `/contributedby/{username}` profile and `/leaderboard`. Meaningful achievements are gated on post-review outcomes (Approved/Imported) so quality isn't sacrificed for volume. Phase 1 = model + backfill + full catalog + levels + display; Phase 2 = dedicated achievements browse/progress page. |
| [Reliable Audio & Subtitle Track Labeling](FEATURE-TRACK-LABELING.md) | 🟢 Core shipped | Audio/subtitle naming, summary parsing/serialization, strict ordinal import validation, edit-sync round-tripping, and naming-helper copy controls ship. The read-only audit command exists; a completed corpus report/cleanup is not recorded here. Decorative output, automated suggestions, and recurring audits remain optional. |
| [User Preferences](FEATURE-USER-PREFERENCES.md) | 🔵 Proposed | Let users opt out of some or all emails via a small per-user preferences framework (email prefs are its first consumer). A typed `UserPreferences` row holds a master switch + per-category flags; both notification services consult a shared preference service before any user-facing send (admin email unaffected). Adds tokenized one-click unsubscribe wired into a real `List-Unsubscribe` header (replacing today's `mailto`) plus a settings UI on `Account/Manage`. Opted-in by default; missing row = all on. Phase 1 = table + enforcement + one-click + UI; Phase 2 = announcements/digest + auditing. |
| [Rapid Disc Intake](FEATURE-RAPID-DISC-INTAKE.md) | 🟢 Shipped | Administrator-only staging and owned contribution promotion are merged in web/tools, with CSV/Amazon lookup, resumable capture, Disc ID backfill, and admin list/edit/image/placeholder management. Staging is not published partial-contribution support. Physical-disc smoke-test completion is not recorded here. |
| [Partial Contributions](FEATURE-PARTIAL-CONTRIBUTIONS.md) | 🔵 Proposed | Let users contribute part of a release/disc and mark what's missing so others can complete it: missing discs (combo/boxset/series), partially-identified discs, and empty/unidentified discs (incl. auto-flagged Engram/API submissions). Partial state is a contributor declaration (canned reason + free-text) persisted in `/data` (`partial { reason, note }` on `disc*.json` and `release.json`), surfaced via detail-page badges and a partial-data hub. Marker writes go DB-first then batch-sync to `/data` like edit suggestions. Missing discs resolve via a retargeted disc-contribution; partial discs via suggested edits. Phase 1 = declaration + persistence + auto-flag + badges + sync; Phase 2 = hub + resolution/clearing; Phase 3 = analytics/notifications. |
| [Global Disc IDs (AACS + DVD)](FEATURE-AACS-DISC-ID.md) | 🟡 Core shipped / follow-ons | Browser capture, per-pressing persistence, add-only backfill/conflict review, data sync, and REST lookup ship for BD/UHD and DVD. ContributionBuddy includes physical-disc backfill and coverage statistics. A public missing-ID campaign view, keydb integration, manual-match fallback, and identifier-based dedup remain backlog. |
| [Disc Scan Rollout](FEATURE-DISC-SCAN-ROLLOUT.md) | 🟡 Built / rollout pending | Optional scanning, parity/similarity dashboards, CLI packaging, and local OdmScanner replay ship. Scan-by-default and CLI-file-upload options are off in checked-in defaults; rollout evidence and public CLI publication remain separate tasks. Log-free phase 3 and unresolved scanner cases remain future work. |
| [Disc Mapping Auto-Suggestions](FEATURE-DISC-MAPPING-AUTO-SUGGESTIONS.md) | 🔵 Proposed | Unimplemented suggestion engine and identify UI for main movies, episode grouping/order, commentary, likely extras, relevance filters, and bulk acceptance. DVDCompare integration is a later candidate. |

## Remaining work by delivery type

- **Implementation:** image master-store migration; user preferences; achievements;
  partial contributions; mapping suggestions; remaining edit-suggestion features;
  Disc ID and canonical-dedup follow-ons.
- **Rollout/publication:** meet the disc-scan parity gate before changing defaults;
  publish scanner packages and decide when to expose CLI uploads; enable user
  edit-notification emails deliberately (`NotifyUsers` is currently false in source).
- **Evidence/sign-off:** record the legacy track-label audit outcome, intake
  physical-disc smoke test, and TV/boxset edit-page audit. Their completion cannot
  be inferred just from the implementation or test files.

## Conventions

- One doc per major initiative. Prefer an ALL-CAPS, hyphenated file name that
  describes the initiative (e.g. `IMAGE-STORAGE-SCALING-PLAN.md`).
- Start each plan with an `## Overview` and, where useful, a status note at the top.
- When you add or retire a plan, update the table above so this stays the single
  place to see the overall roadmap.
- When a plan is fully delivered, mark it 🟢 **Shipped** here (optionally move
  detail into code/docs) rather than silently deleting the doc.
