# Disc Deduplication — Future Work

> Status: **Partial / backlog** (source reviewed 2026-10-29). These items build on the shipped
> [Canonical Disc Deduplication (Phase 4)](PHASE-4-CANONICAL-DISC-DEDUPLICATION.md)
> model. Exact-match copy detection is implemented; the other candidates below
> are not scheduled.

## Context

Phase 4 shipped the canonical disc model: a `Disc` is keyed by a unique
`(Format, ContentHash)` and shared across releases via the `ReleaseDisc` join table.
Import- and contribution-time dedup already collapse identical disc content into a
single canonical row. The items below are the "once canonical discs exist" enhancements
that were deferred out of Phase 4.

Relevant existing entry points:
- Entities: `Disc`, `ReleaseDisc`, `Track`, `Chapter` (`code/TheDiscDb.Core/InputModels/`).
- Canonical resolution on import: `DataImporter.ToReleaseDisc`
  (`code/TheDiscDb.Data.Import/DataImporter.cs`).
- Contribution disc hashing/upload: `UserContributionDisc`
  (`code/TheDiscDb.Data/Models/UserContributionDisc.cs`), `DiscHash`
  (`code/TheDiscDb/GraphQL/Contribute/Models/DiscHash.cs`).

## Candidate workstreams

### 1. Disc versioning

Track historical updates to canonical disc data so a correction to shared content is
auditable and reversible.

```
DiscVersion
├── DiscId (FK)
├── VersionNumber
├── ChangedAt
├── ChangedBy
└── Changes (JSON diff)
```

Because a canonical disc is shared across releases, an edit propagates everywhere —
versioning gives a safety net and an audit trail. Consider reusing the existing
edit-suggestion / change-tracking infrastructure rather than a bespoke table.

> Auditing note: this is a user-action / state-change feature — build audit logging in
> from the start.

### 2. Smart copy detection

> **Status: Partial.** Exact-match detection and the copy confirmation already ship
> in [`AddDisc.TryCopyFromExistingDisc`](../code/TheDiscDb.Client/Pages/Contribute/AddDisc.razor.cs).
> Only partial/near-match suggestions remain proposed.

The contribution flow hashes the inserted disc, offers to copy an existing matching
disc, and only attempts intake matching when no published match exists. Import then
resolves the canonical disc by format/content hash.

Remaining: detect similar but nonidentical discs and ask "This looks similar to
disc X. Are you sure you want a new one?" Do not treat this as missing exact-match
deduplication or automatically merge discs based on similarity.

### 3. Moderator merge tooling

Admin surface to consolidate discs that should have been canonicalized but weren't
(e.g. slightly different hashes for effectively identical content): "these N discs are
likely duplicates — merge them?" Merging re-points the affected `ReleaseDisc` rows to a
single canonical `Disc` and deletes the losers.

> Auditing note: destructive/state-changing admin action — log who merged what.

### 4. Automated duplicate detection

Background/reporting job that surfaces canonical discs that are probably the same
content (near-identical track/chapter data) for a moderator to review via (3).

### 5. Analytics & insights

Reporting on disc reuse now that the data supports it:

```sql
-- Most-referenced discs
SELECT d.*, COUNT(rd.Id) AS ReferenceCount
FROM Discs d
LEFT JOIN ReleaseDiscs rd ON d.Id = rd.DiscId
GROUP BY d.Id
ORDER BY ReferenceCount DESC;

-- Box sets with high disc reuse
SELECT r.Title, COUNT(DISTINCT d.Id) AS UniqueDiscs, COUNT(rd.Id) AS TotalDiscs
FROM Releases r
JOIN ReleaseDiscs rd ON r.Id = rd.ReleaseId
JOIN Discs d ON rd.DiscId = d.Id
GROUP BY r.Id
HAVING COUNT(rd.Id) > COUNT(DISTINCT d.Id);
```

### 6. Optional denormalization

If frequently-accessed fields (`Format`, `ContentHash`) on the canonical `Disc` become a
query-perf bottleneck when read through `ReleaseDisc`, denormalize them onto `ReleaseDisc`
with an eventual-consistency job keeping them in sync. Only pursue if profiling shows a
need.
