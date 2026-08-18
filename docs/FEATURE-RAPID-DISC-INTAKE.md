# Feature Spec: Rapid Disc Intake

## Overview

Rapid Disc Intake lets an administrator scan a physical release without identifying its movie,
episode, or extra titles. ContributionBuddy captures release metadata, images, disc hashes, Disc
IDs, and MakeMKV scan logs into staging tables. When a contributor later scans a matching disc,
the normal web contribution flow reuses that evidence and continues through identification,
review, and import.

## Status

🟡 **In progress** — implementation is on the `feature/rapid-disc-intake` branches in the `web`
and `tools` repositories.

## Goals

- Make multi-disc source intake fast and resumable.
- Preserve raw evidence without publishing incomplete domain records.
- Reuse staged evidence through the existing contribution ownership, identification, review, and
  import workflow.
- Deduplicate repeated release and disc scans while retaining evidence history.

## Intake workflow

1. An administrator runs ContributionBuddy's `intake` command.
2. The command resolves `ContributionBuddy:DefaultUserEmail` and verifies the user is an
   administrator.
3. It scans the first disc's content hash and Global Disc ID before collecting release metadata.
   Discs already present in the published database or intake staging are reported, ejected, and
   skipped.
4. For a new disc, it asks for an optional ASIN, imports and caches available Amazon metadata by
   ASIN, and uses the imported title to present the top three TMDB candidates or accept a manual
   TMDB ID.
5. It captures UPC, title/date, locale (default `en-us`), region (default `1`), a downloadable
   front-image URL, an optional downloadable back-image URL, and one or more physical discs.
6. Each disc records its format, content hash, optional Global Disc ID, optional name/slug, and a
   validated MakeMKV scan log.
7. The save operation downloads supported JPG, PNG, or WebP images, creates or reuses normalized
   intake release/disc rows, persists immutable
   evidence assets, and records warnings for published or staged duplicates.
8. Interrupted work remains in a resumable workspace draft. The draft is removed only after a
   successful save.

## Promotion workflow

- Release lookup requires an exact normalized external-provider, external-ID, and UPC match.
- Disc lookup uses format plus content hash.
- Evidence with an exact Global Disc ID is preferred. Otherwise the latest compatible evidence is
  used and the mismatch is retained as provenance for review.
- Published database matches take precedence over staged intake.
- Promotion is restricted to the contribution owner while the contribution is editable.
- Repeated promotion is idempotent, contributor-uploaded logs are preserved, and deleted promoted
  discs can be restored without duplicating evidence.
- Normal and boxset contributions use the same staging model.

## Persistence

The intake schema consists of:

- `IntakeRelease` and `IntakeReleaseDisc` for staged release structure.
- Canonical `IntakeDisc` rows keyed by normalized format and content hash.
- `IntakeDiscEvidence` for scan logs, Disc IDs, and captured assets.
- `IntakePromotion` for release/disc promotion provenance and status.

Intake rows are staging evidence, not published domain data. Approved contributions and the
`data` repository remain authoritative.

## Security and failure behavior

- Source capture is administrator-only.
- Web promotion enforces authentication, ownership, and editable contribution status.
- Invalid MakeMKV logs or asset failures must not leave partial database writes.
- Natural identifiers and evidence-set IDs are used where domain integer IDs would be unstable
  across rebuilds.

## Non-goals

- Directly publishing unidentified discs.
- Contributor-facing bulk intake.
- Partial-state declarations, missing-disc placeholders, badges, or a partial-data hub.
- Replacing contribution review or title identification.

## Verification

- Unit tests cover new intake, rescans, canonical-disc reuse, changed disc identity at an index, invalid-log
  rollback, exact/fallback Disc ID evidence, ownership/status guards, idempotent promotion,
  contributor-log preservation, published database precedence, and boxsets.
- An end-to-end smoke test should stage a multi-disc release, scan a matching disc in the web app,
  verify metadata/log reuse, and confirm repeated promotion creates no duplicates.
