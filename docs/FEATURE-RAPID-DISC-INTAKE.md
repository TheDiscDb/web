# Feature Spec: Rapid Disc Intake

## Overview

Rapid Disc Intake lets an administrator scan a physical release without identifying its movie,
episode, or extra titles. ContributionBuddy captures release metadata, images, disc hashes, Disc
IDs, and MakeMKV scan logs into staging tables. When a contributor later scans a matching disc,
the normal web contribution flow reuses that evidence and continues through identification,
review, and import.

## Status

🟢 **Shipped** (source reviewed 2026-10-29). The implementation and later
enhancements are merged into `main` in both `web` and `tools`; it is not waiting
on the old `feature/rapid-disc-intake` branches.

Implementation evidence:
- Web staging [models](../code/TheDiscDb.Data/Models/IntakeRelease.cs) and migrations,
  [matching/promotion service](../code/TheDiscDb.Contributions/Contributions/IntakeMatchingService.cs),
  and [admin management service](../code/TheDiscDb/Services/Admin/IntakeAdminService.cs).
- ContributionBuddy [capture command](https://github.com/TheDiscDb/tools/blob/main/tools/ContributionBuddy/Intake/IntakeTask.cs),
  persistence service, lookup CSV support, and Disc ID backfill coordination.
  The [tools README](https://github.com/TheDiscDb/tools#rapid-disc-intake) describes the current operator flow.

Physical-disc smoke-test completion is not recorded here. Merged implementation
does not establish which production database migrations/configuration are active.

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
   skipped. Missing published Disc IDs are backfilled through the approved-change
   sync rail; conflicts go to review, and ambiguous published matches require selecting
   the physical release.
4. For a new disc, it asks for UPC and reads the configured lookup CSV. Matching
   rows supply ASIN, external ID, type, format, and optional disc count; absent
   values use the normal prompts. External ID `0` stages a release without TMDB.
   Amazon metadata is cached by ASIN and its cleaned title seeds candidate lookup.
5. It captures title/date, fixes locale to `en-us` and region to `1`, and uses available
   Amazon front/back image URLs without prompting for missing images. A CSV disc
   count controls how many physical discs to capture.
6. Each disc records its format, content hash, optional Global Disc ID, optional name/slug, and a
   validated MakeMKV scan log.
7. The save operation downloads supported JPG, PNG, or WebP images, creates or reuses normalized
   intake release/disc rows, persists immutable
   evidence assets, and records warnings for published or staged duplicates.
8. Interrupted work remains in a resumable workspace draft. The draft is removed only after a
   successful save. Saving has no final confirmation and immediately returns to
   intake for the next release.

## Promotion workflow

- Automatic release metadata lookup requires TMDB plus an exact normalized
  external-ID and UPC match. CLI support for no-TMDB intake is not a claim that
  this release-prefill lookup supports arbitrary providers.
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

## Shipped admin management

`/admin/intake` and its detail/edit pages support search/status filtering, release
metadata edits, disc-link edits and ordering, shared-format confirmation, front/back
image management, and deletion of staged releases. `IntakeReleaseDisc.IsPlaceholder`
and `FailureReason` represent incomplete **staging** slots; they are not published
partial-release markers or the contributor-facing partial-data hub.

## Security and failure behavior

- Source capture is administrator-only.
- Web promotion enforces authentication, ownership, and editable contribution status.
- Invalid MakeMKV logs or asset failures must not leave partial database writes.
- Natural identifiers and evidence-set IDs are used where domain integer IDs would be unstable
  across rebuilds.

## Non-goals

- Directly publishing unidentified discs.
- Contributor-facing bulk intake.
- Published partial-state declarations, missing-disc help-wanted markers, badges, or
  a partial-data hub; those belong to [Partial Contributions](FEATURE-PARTIAL-CONTRIBUTIONS.md).
- Replacing contribution review or title identification.

## Verification

- Unit tests cover new intake, rescans, canonical-disc reuse, changed disc identity at an index, invalid-log
  rollback, exact/fallback Disc ID evidence, ownership/status guards, idempotent promotion,
  contributor-log preservation, published database precedence, and boxsets.
- An end-to-end smoke test should stage a multi-disc release, scan a matching disc in the web app,
  verify metadata/log reuse, and confirm repeated promotion creates no duplicates.
