# Phase 4: Canonical Disc Deduplication

## Overview

Phase 4 refactors the disc storage model to eliminate duplication of identical disc content across releases. Currently, if two releases contain discs with identical track and chapter data, that data is stored twice. This phase introduces a canonical disc model that stores content once and references it from multiple releases.

## Problem

Today's schema stores tracks and chapters directly on the `Disc` table. When two releases have identical disc content:
- **Batwoman Season 1** (2019 Blu-ray US) — Disc 1
- **Batwoman Season 1** (2020 Blu-ray UK) — Disc 1

Both discs have identical tracks and chapters → stored twice in the database.

For large collections (box sets, anthology releases), this duplication becomes significant storage overhead.

## Solution: Canonical Disc Model

Separate **disc content** (what's on the disc) from **release context** (how it appears in a release).

### Schema Design

#### Disc Table (The Content)
The canonical disc, keyed by format + content hash.

```
Disc
├── Id (PK)
├── Format (varchar, e.g., "Blu-ray", "DVD", "4K", "UHD")
├── ContentHash (varchar, unique index with Format)
├── CreatedAt (datetime)
└── Tracks[] (collection, FK: DiscId)
└── Chapters[] (collection, FK: DiscId)
```

- **ContentHash**: SHA256 hash of serialized track/chapter data (deterministic, stable)
- **Unique index**: `(Format, ContentHash)` ensures one row per unique format + content combination
- **Tracks/Chapters**: These FKs change from the old `DiscId` to the new canonical disc

#### ReleaseDisc Table (The Instance)
New join table connecting a Release to a Disc, with release-specific metadata.

```
ReleaseDisc
├── Id (PK)
├── ReleaseId (FK → Release)
├── DiscId (FK → Disc)
├── Name (varchar, e.g., "Disc 1", "DVD 1", "Bonus Content")
├── Slug (varchar, e.g., "disc-1")
└── Index (int, order within release)
```

- **Name**: Release-specific disc label (how it appears in *this* release)
- **Slug**: URL-friendly identifier (how it appears in *this* release)
- **Index**: Ordering (how it appears in *this* release)

#### Existing Tables (Updated)

**Track**
```
Track
├── DiscId (FK → Disc) ← changed from old DiscId
└── ... (all existing columns: CodecString, Duration, Language, Index, etc.)
```

**Chapter**
```
Chapter
├── DiscId (FK → Disc) ← changed from old DiscId
└── ... (all existing columns: Index, TimeInMs, Title, etc.)
```

## Rebuild Process

### Step 1: Calculate Content Hashes

For every existing disc in the database, compute a deterministic content hash from its tracks and chapters.

```csharp
private string GenerateContentHash(Disc disc, IEnumerable<Track> tracks, IEnumerable<Chapter> chapters)
{
    var hashInput = new
    {
        Tracks = tracks
            .OrderBy(t => t.Index)
            .Select(t => new { t.CodecString, t.Duration, t.Language, t.Index })
            .ToList(),
        ChapterCount = chapters.Count(),
        Chapters = chapters
            .OrderBy(c => c.Index)
            .Select(c => new { c.Index, c.TimeInMs })
            .ToList()
    };
    
    var json = JsonSerializer.Serialize(hashInput);
    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }
}
```

### Step 2: Group by (Format, ContentHash)

Group all discs by `(Format, ContentHash)`. Each group will become one canonical `Disc` row.

```csharp
var discsByFormatAndHash = db.Discs
    .Include(d => d.Tracks)
    .Include(d => d.Chapters)
    .GroupBy(d => new { 
        d.Format, 
        Hash = GenerateContentHash(d, d.Tracks, d.Chapters) 
    })
    .ToList();
```

### Step 3: Create Canonical Discs

For each `(Format, ContentHash)` group, create one `Disc` row. Reuse the tracks and chapters from the first disc in the group (they're identical by definition).

```csharp
var newDiscs = new List<Disc>();
foreach (var group in discsByFormatAndHash)
{
    var firstDisc = group.First();
    var canonicalDisc = new Disc
    {
        Format = group.Key.Format,
        ContentHash = group.Key.Hash,
        CreatedAt = DateTime.UtcNow
        // Tracks and Chapters are reused from firstDisc
    };
    newDiscs.Add(canonicalDisc);
}
db.Discs.AddRange(newDiscs);
await db.SaveChangesAsync();
```

### Step 4: Create ReleaseDisc Rows

For each old disc that belonged to a release, create a `ReleaseDisc` row linking that release to the corresponding canonical disc.

```csharp
var releaseDiscs = new List<ReleaseDisc>();
foreach (var group in discsByFormatAndHash)
{
    var canonicalDisc = newDiscs[newDiscs.Count - 1]; // just created
    
    foreach (var oldDisc in group)
    {
        var releaseDisc = new ReleaseDisc
        {
            ReleaseId = oldDisc.ReleaseId,
            DiscId = canonicalDisc.Id,
            Name = oldDisc.Name,
            Slug = oldDisc.Slug,
            Index = oldDisc.Index ?? 0
        };
        releaseDiscs.Add(releaseDisc);
    }
}
db.ReleaseDiscs.AddRange(releaseDiscs);
await db.SaveChangesAsync();
```

### Step 5: Database Migration

```sql
-- Create CanonicalDisc (renamed to Disc conceptually, old Disc deleted)
-- Create ReleaseDisc
-- Update Track.DiscId FK to point to canonical Disc
-- Update Chapter.DiscId FK to point to canonical Disc
-- Drop old Disc columns (ReleaseId, etc.)
```

## Benefits

### 1. Storage Efficiency
- Single storage of each unique disc content
- Especially valuable for box sets, anthology releases, and multi-format editions
- Example: 200-disc box set with many duplicates could reduce disc records from 200 to ~20 unique canonicals

### 2. Querying & Analytics
```sql
-- Find all releases containing a specific disc
SELECT DISTINCT r.* 
FROM Releases r
JOIN ReleaseDiscs rd ON r.Id = rd.ReleaseId
WHERE rd.DiscId = @discId;

-- Find releases with duplicate discs
SELECT r.Id, COUNT(DISTINCT rd.DiscId) as DiscCount
FROM Releases r
JOIN ReleaseDiscs rd ON r.Id = rd.ReleaseId
GROUP BY r.Id
HAVING COUNT(DISTINCT rd.DiscId) < (SELECT COUNT(*) FROM ReleaseDiscs WHERE ReleaseId = r.Id);

-- Identify format variants of the same content
SELECT ContentHash, COUNT(DISTINCT Format) as FormatCount, 
       STRING_AGG(Format, ', ') as Formats
FROM Discs
GROUP BY ContentHash
HAVING COUNT(DISTINCT Format) > 1;
```

### 3. Copy Flow Simplification
When a user copies a disc in a contribution, there's no need to copy track/chapter data—just reference the canonical disc ID.

**Before Phase 4:**
```csharp
var newDisc = new Disc
{
    ReleaseId = targetReleaseId,
    Format = sourceDisc.Format,
    Tracks = sourceDisc.Tracks.Select(t => new Track(t)).ToList(),
    Chapters = sourceDisc.Chapters.Select(c => new Chapter(c)).ToList()
};
```

**After Phase 4:**
```csharp
var newReleaseDisc = new ReleaseDisc
{
    ReleaseId = targetReleaseId,
    DiscId = sourceReleaseDisc.DiscId, // just reference
    Name = sourceReleaseDisc.Name,
    Slug = sourceReleaseDisc.Slug,
    Index = sourceReleaseDisc.Index
};
```

### 4. Quality Control & Curation
Correct track/chapter metadata once in the canonical disc; it propagates to all releases immediately.

```csharp
var disc = await db.Discs.Find(discId);
disc.Tracks[0].Title = "Corrected Title";
await db.SaveChangesAsync();
// All releases using this disc see the fix
```

### 5. Foundation for Future Deduplication Workflows
- Moderator tools: "These 5 discs are likely duplicates (similar hash)—merge them?"
- Automated detection: Find discs that should be consolidated
- Versioning: Track when canonical disc data was updated

## GraphQL & API Impact

### Query Layers (Minimal Change)

The GraphQL schema doesn't need to change:

```graphql
type Release {
  id: ID!
  title: String!
  discs: [ReleaseDisc!]!
}

type ReleaseDisc {
  id: ID!
  name: String!
  slug: String!
  format: String!
  tracks: [Track!]!
  chapters: [Chapter!]!
}

type Track {
  id: ID!
  index: Int!
  codecString: String!
  duration: Int!
  language: String
}
```

Resolvers automatically navigate the foreign keys:

```csharp
[GraphQLName("tracks")]
public IEnumerable<Track> GetTracks([Parent] ReleaseDisc releaseDisc)
    => db.Tracks.Where(t => t.DiscId == releaseDisc.DiscId);

[GraphQLName("format")]
public string GetFormat([Parent] ReleaseDisc releaseDisc)
    => db.Discs.Find(releaseDisc.DiscId)?.Format ?? "";
```

### Contribution Model

`UserContributionDisc` can reference the canonical disc directly:

```csharp
public class UserContributionDisc
{
    public int? DiscId { get; set; } // optional: if copying, point to canonical disc
    public string? ExistingDiscPath { get; set; } // keep for backwards compat
    // ... existing fields
}
```

When a user finishes a copied-disc contribution, import just sets `DiscId` instead of copying tracks/chapters.

## Timeline & Rollout

- **Requires**: Full database rebuild (not a phased/backwards-compatible change)
- **When to implement**: After Phase 2 (copy flow) is stable and in production
- **Data prep**: Can be scripted and tested in dev/staging without downtime risk (it's a rebuild, not a migration)
- **Code changes**: Modest (mostly navigation changes in resolvers)
- **Risk**: Low (clear separation of old schema → new schema, easy to validate)

## Data Repo Deduplication

The database model is only half of the story. The `data/` repo also contains duplicate disc artifacts today:
- `disc*.json`
- `disc*.txt`
- `disc*-summary.txt`

To dedupe those files, introduce a `disc*.ref` file that points at the canonical disc bundle for a release:

```json
{
  "releasePath": "series/Batwoman (2019)/the-complete-first-season-blu-ray",
  "disc": "disc01"
}
```

- `releasePath` identifies the release folder relative to `data/data/`
- `disc` identifies the disc stem within that release
- the ref represents the full bundle: `disc01.json`, `disc01.txt`, and `disc01-summary.txt`

That gives us a "first one wins" rule:
1. The first valid disc bundle in a release stays as the canonical files.
2. Later duplicates become tiny ref files.
3. Import resolution follows the ref back to the canonical bundle.

This keeps the repo readable while eliminating duplicated payloads across all three disc files.

## Future Enhancements

Once canonical discs exist:

### Disc Versioning
Track historical updates to canonical disc data:
```
DiscVersion
├── DiscId (FK)
├── VersionNumber
├── ChangedAt
├── ChangedBy
└── Changes (JSON diff)
```

### Smart Copy Detection
When a user uploads a disc in a contribution, compare its hash against existing canonical discs:
- Exact match → "This disc is already in the database. Link it?"
- Partial match → "This looks similar to disc X. Sure you want a new one?"

### Analytics & Insights
```sql
-- Most-referenced discs
SELECT d.*, COUNT(rd.Id) as ReferenceCount
FROM Discs d
LEFT JOIN ReleaseDiscs rd ON d.Id = rd.DiscId
GROUP BY d.Id
ORDER BY ReferenceCount DESC;

-- Box sets with high disc reuse
SELECT r.Title, COUNT(DISTINCT d.Id) as UniqueDiscs, COUNT(rd.Id) as TotalDiscs
FROM Releases r
JOIN ReleaseDiscs rd ON r.Id = rd.ReleaseId
JOIN Discs d ON rd.DiscId = d.Id
GROUP BY r.Id
HAVING COUNT(rd.Id) > COUNT(DISTINCT d.Id);
```

### Future Denormalization
If needed, could denormalize frequently-accessed fields (format, content hash) onto `ReleaseDisc` for query performance, with an eventual consistency job keeping them in sync.
