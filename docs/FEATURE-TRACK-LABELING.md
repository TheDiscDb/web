# Feature Spec: Reliable Audio & Subtitle Track Labeling

## 1. Summary

Audio- and subtitle-track labeling in `disc*-summary.txt` is implemented end-to-end.
The raw track list originates from the MakeMKV logs (`disc*.txt`); import resolves
labels by their 1-based, per-type ordinal and rejects references outside that list.
This catches structurally invalid references, not a human assigning the wrong name
to a valid ordinal.

Labels stay in the summary file (per the maintainer's preference), but the format is tightened
so that:
- Audio labeling keeps its current index semantics (backward compatible with all existing data).
- Subtitle labeling is added as a parallel, symmetric mechanism.
- The importer **validates** every reference against `disc*.txt` and hard-fails on mismatch.
- A read-only **audit tool** flags broken and suspicious legacy audio labels so
  any correction effort can be driven by evidence.

## 2. Status

🟢 **Core shipped** (source reviewed 2026-10-29). Subtitle labeling and strict import
validation are merged, including the later audio/subtitle naming fixes and clipboard
controls in [web #123](https://github.com/TheDiscDb/web/pull/123).
The audit command is implemented; a completed corpus report or cleanup decision is
not recorded in these repositories.

| Phase | State / remaining work |
|-------|------------------------|
| **Phase 0** | Tool shipped: ContributionBuddy `audit-tracks <root> --output <report.csv>`. Record a corpus run and triage/correction decision separately; tool existence is not proof the audit was completed. |
| **Phase 1** | Shipped: model/migration, contribution UI and mutations, summary serializer/parser, strict import validation, edit-sync round-trip, and naming-helper display/copy. Audio ordinal semantics are unchanged. |
| **Phase 2** *(optional)* | Parser already strips decorative ` #` comments. Serializers still emit bare labels; automatic decorative output, further hand-edit ergonomics, and audit-driven corrections remain optional. |

Implementation evidence:
- [Contribution subtitle model](../code/TheDiscDb.Data/Models/UserContributionSubtitleTrack.cs)
  and [mutation](../code/TheDiscDb/GraphQL/Contribute/Mutations/AddSubtitleTrackToItem.cs).
- [Identify UI](../code/TheDiscDb.Client/Pages/Contribute/IdentifyDiscItems.razor.cs)
  and [naming helper](../code/TheDiscDb.Client/Pages/Contribute/DiscNamingItemDetail.razor.cs).
- [Parser](../code/TheDiscDb.Core/SummaryFileParser.cs),
  [import mapping](../code/TheDiscDb.Import/ImportHelper.cs), and
  [serializer](../code/TheDiscDb/Services/Admin/SummaryFileSerializer.cs).
- Sibling tools:
  [audit command](https://github.com/TheDiscDb/tools/blob/main/tools/ContributionBuddy/TrackLabelAuditTask.cs) and
  [edit-sync writer](https://github.com/TheDiscDb/tools/blob/main/tools/ContributionBuddy/EditSuggestions/EditSuggestionFileApplier.cs).

## 3. Background: how it works today

### 3.1 The label format
`disc*-summary.txt` carries lines like:
```
AudioTrack[2]: Director's Commentary
SubtitleTrack[1]: English (SDH)
```
- **Writers:** web and ContributionBuddy `SummaryFileSerializer` emit both kinds.
- **Parser:** `TheDiscDb.Core/SummaryFileParser.cs` reads both kinds and strips
  optional trailing ` #` comments.

### 3.2 What `[n]` means at import
`TheDiscDb.Import/ImportHelper.cs` (`DiscFileFinalizer.Map`) resolves `AudioTrack[n]` as the
**Nth audio stream (1-based) in MakeMKV-log order**:
```csharp
var audioTracks = match.Tracks.Where(t => t.Type == "Audio");
var foundTrack = audioTracks.ElementAtOrDefault(track.Index - 1);
foundTrack.Description = track.Name;   // label written to the track's Description
```

### 3.3 Two "generations" of data (the crux)
- **Contribution-flow data (reliable by construction).** The contribution UI
  (`IdentifyDiscItems.razor.cs`) builds the audio list directly from
  `title.Segments.Where(Type == "Audio")`, numbering them `1..N` and showing each track's
  language/codec. The user labels by **picking from that list** — the *site* computes the index
  from `disc*.txt`, not the human. The importer reads it back with the identical rule, so writer
  and reader always agree.
- **Legacy hand-authored data (the risk).** A human counted audio tracks in the **MakeMKV GUI**
  and typed `AudioTrack[n]`. It is correct only if that count matched the log's audio-stream
  order. MakeMKV's GUI **parent/child audio grouping** (nested/associated tracks) can make a
  human skip or double-count, so a legacy label can **silently** point at the wrong stream.

### 3.4 What contributions store
- `UserContributionDiscItem.AudioTracks` → `UserContributionAudioTrack { Index (int), Title }`.
  `Index` = the audio-only 1-based ordinal; `Title` = the label.
- `UserContributionDiscItem.SubtitleTracks` stores
  `UserContributionSubtitleTrack { Index, Title }` with the same per-type ordinal rule.
- **No type/language/codec is stored** on the contribution row; those come live from `disc*.txt`
  via `GetDiscLogs`, so the serializer already has the log at write time (it can emit decorative
  attributes *and* validate).
- Published-track description edits round-trip through ContributionBuddy's
  `EditSuggestionFileApplier`: it updates disc JSON and the corresponding summary
  label, translating the full stream index to a 1-based per-type ordinal.

### 3.5 Remaining limitations
1. `[n]` is a **per-type ordinal**, not the full MakeMKV stream index.
2. A valid ordinal can still carry a semantically wrong hand-authored label.
   Structural validation cannot distinguish same-language main/commentary tracks.
3. The DB/edit-suggestion model keys tracks by the full stream index, so the sync
   writer must retain its per-type translation.

## 4. Implemented design

### 4.1 Chosen approach — per-type ordinals ("Approach 2")
Keep the audio index semantics exactly as they are today (the Nth audio stream, 1-based, in log
order) and add a **symmetric** subtitle mechanism:
```
AudioTrack[1]: English 5.1
AudioTrack[2]: Director's Commentary
SubtitleTrack[1]: English
SubtitleTrack[2]: English (SDH)
```
- `AudioTrack[n]` → Nth `Audio` stream in log order (unchanged).
- `SubtitleTrack[n]` → Nth `Subtitles` stream in log order (new).

This was chosen over a "full MakeMKV stream index" scheme because it is **backward compatible**
with all existing audio data and maps cleanly onto the existing contribution storage (keep
`UserContributionAudioTrack`, add a sibling table) with **no migration of existing rows**. The
trade-off — it keeps a per-type ordinal that the importer must translate to the DB's full-stream
index — is accepted.

### 4.2 Parseability & the role of attributes
The **only machine-critical tokens** in a label line are the integer index and the free-text
label. Grammar:
```
<Type>Track[<int>]: <label>[ # <decorative attributes>]
```
- `<Type>` ∈ {`Audio`, `Subtitle`}.
- `<label>` is free text captured up to an optional ` #` comment, so codecs/labels containing
  spaces or colons never break parsing.
- Any `type · language · codec` after `#` is a **decorative, regenerated** comment: the writer
  may emit it to aid humans; the **reader ignores it**. It is never the source of truth.

The original proposed combined regex was:
```
^(Audio|Subtitle)Track\[(\d+)\]:\s*([^#]*?)\s*(?:#.*)?$
```
→ group 1 = type, group 2 = index (int), group 3 = clean label.

The implementation instead retains separate audio/subtitle patterns and strips
comments in `CleanTrackName`, starting at the literal ` #` delimiter. A bare `#`
inside a label is not a comment. Serializers currently emit no decorative suffix.

**Validation is always done against `disc*.txt`, not the string.** `disc*.txt` is committed and
immutable per release, so it is the source of truth; a stale hand-written comment is harmless.

### 4.3 Import mapping & validation
For each `AudioTrack[n]` / `SubtitleTrack[n]`:
1. Filter the matched title's streams to the corresponding type (`Audio` / `Subtitles`).
2. Take the Nth (1-based). If it does not exist → **hard-fail** the import with a clear message
   (mirrors the current audio out-of-range throw; now also covers subtitles).
3. Write the label to that track's `Description`.

Normalize the MakeMKV/DB type strings: the log and DB use **`Subtitles`** (plural); some
edit-suggestion code paths use `Subtitle` (singular). The importer and matching logic must treat
them equivalently.

### 4.4 Contribution storage
- `UserContributionSubtitleTrack { Id, Index (int), Title (string), Item }` mirrors
  `UserContributionAudioTrack`.
- `UserContributionDiscItem.SubtitleTracks` is persisted by the
  `AddSubtitleTracks` migration. **No change** to `UserContributionAudioTrack` or its
  existing rows.

### 4.5 GraphQL
- `AddSubtitleTrackToItem(contributionId, discId, itemId, trackIndex, trackName)` mirrors
  `AddAudioTrackToItem` (upsert by `Index`).
- The client schema/operation (`ContributionGraphQL`) includes subtitle labels.

### 4.6 Contribution UI
- `IdentifyDiscItems.razor.cs` builds a subtitle list from
  `title.Segments.Where(Type == "Subtitles")` (numbered `1..N`, same pattern as audio) and lets the
  user label them; labels persist via `AddSubtitleTrackToItem`.
- `DiscNamingItemDetail` displays and copies subtitle labels, symmetric to audio.

### 4.7 Serializer & parser
- `SummaryFileSerializer` emits `SubtitleTrack[n]: {label}` after audio lines for
  each labeled subtitle track. Automatic decorative output remains optional.
- `SummaryFileParser` parses `SubtitleTrack[n]` into `DiscFileItem.SubtitleTrackNames`
  and strips trailing ` #` comments from both types. Existing audio ordinals remain
  backward compatible.

### 4.8 Admin edit
- `TracksEdit` treats audio + subtitle as describable. The sync writer persists
  descriptions in JSON and `AudioTrack[n]` / `SubtitleTrack[n]` summary lines;
  serializer/parser/import support is implemented.

### 4.9 Phase 0 — audit & validation report
A tool (or admin report) that runs over the data repo:
1. For each disc, parse `disc*-summary.txt` and `disc*.txt`.
2. Resolve each `AudioTrack[n]` to its target audio stream (Nth audio, log order).
3. Flag:
   - **Structural errors** — index out of range / more audio labels than audio streams (these
     already throw at import).
   - **Suspicious labels** — label text conflicts with the resolved stream's attributes, e.g. a
     language name in the label (`French`, `Español`) that disagrees with the stream language, or
     commentary keywords (`commentary`, `director`, `isolated score`) on a stream whose
     attributes suggest it is the primary track.
4. Emit a report: counts, per-title findings, and a confidence bucket (broken / suspicious / ok).

**Limitation:** two *same-language* audio tracks (main vs commentary) frequently cannot be told
apart from the log alone. Those are flagged for **human review/listening** — the audit cannot
auto-correct them.

## 5. Behavior & rules

- Audio label semantics are **unchanged**; the format change is additive (subtitles + optional
  decorative comments + validation).
- The parser must remain tolerant of existing audio lines with **no** attributes/comments.
- Import is **strict**: any label that cannot be resolved to an existing stream of the right type
  fails the import loudly rather than importing a mislabeled or unlabeled track.
- `disc*.txt` is the single source of truth for track identity; decorative attributes in the
  summary are never authoritative.

## 6. Risks & considerations

- **Legacy correctness needs a recorded Phase 0 report.** The audit tool's presence
  does not establish that a corpus run or correction pass has occurred.
- **Same-language main-vs-commentary ambiguity** is inherent and cannot be resolved from logs;
  expect a residual set needing human ears.
- **Hard-fail imports** could surface pre-existing latent errors in the data repo (labels that
  were always wrong). That is desirable but may require a cleanup pass; the audit front-runs it.
- **DB→data-repo sync** for track `Description` edits is implemented for both types
  and has focused `EditSuggestionFileApplierTests`; preserve that round-trip behavior.
- **Type-string normalization** (`Subtitle` vs `Subtitles`) must be handled consistently across
  importer, matching, and edit-suggestion code to avoid new mismatches.

## 7. Open questions

- Whether to add decorative `# type · language · codec` output; the shipped choice
  is bare labels with tolerant comment parsing.
- Whether to extend the audit's existing language/commentary heuristics.
- Should the audit also verify **audio** labels created by the new contribution flow (expected
  100% clean) as a control group, to validate the heuristics?
- Record the corpus audit findings and decide which cases need listening/manual
  correction. No automatic legacy correction is established by this source review.

## 8. Rollout / phases

- **Phase 0** — audit tooling shipped; corpus report and cleanup sign-off unverified.
- **Phase 1** — shipped: subtitle labeling end-to-end + strict import validation.
- **Phase 2** *(optional)* — decorative attributes, hand-edit ergonomics, and audit-driven
  correction of legacy data proven safely fixable.

## 9. Future enhancements

- Move to a single **full MakeMKV stream index** across the whole stack (summary, contribution
  store, DB, edit-suggestions) to eliminate the per-type ordinal translation entirely — a larger,
  unifying refactor deferred in favor of the backward-compatible Approach 2.
- Auto-suggested labels from MakeMKV-provided stream names/flags (e.g. commentary flags) to
  pre-fill the contribution UI.
- Audit as a recurring CI check over the data repo to catch regressions.

## 10. Auditing

Phase 0 is itself an audit of the *data*. Separately, the Phase 1 label-editing paths
(contribution submissions and admin `TracksEdit`) are user-facing state changes; award/edit
actions should be captured by the existing contribution/edit-suggestion history + audit
mechanisms so track-label changes are traceable.
