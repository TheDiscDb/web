# Feature Spec: Reliable Audio & Subtitle Track Labeling

## 1. Summary

Make audio- and subtitle-track labeling in `disc*-summary.txt` **reliable and unambiguous**,
and extend labeling to **subtitle tracks** (which are not supported today). The raw track list
originates from the MakeMKV logs (`disc*.txt`); labels should reference those tracks in a way
that can be validated at import so a label can never silently land on the wrong track.

Labels stay in the summary file (per the maintainer's preference), but the format is tightened
so that:
- Audio labeling keeps its current index semantics (backward compatible with all existing data).
- Subtitle labeling is added as a parallel, symmetric mechanism.
- The importer **validates** every reference against `disc*.txt` and hard-fails on mismatch.
- A one-time **audit** first measures how much *legacy* hand-authored audio data is actually
  mislabeled, so any correction effort is driven by real numbers.

## 2. Status

🔵 **Proposed** — no implementation started.

| Phase | Scope |
|-------|-------|
| **Phase 0** | Audit & validation report: scan every `disc*-summary.txt` + `disc*.txt`, resolve each label, flag suspicious/structurally-broken ones, and quantify legacy correctness. Decide from the numbers whether/what to correct. |
| **Phase 1** | Subtitle labeling end-to-end (data model, contribution UI, GraphQL mutation, serializer, parser, importer) + hard-fail import validation for audio **and** subtitles. Audio format unchanged. |
| **Phase 2** *(optional)* | Decorative human-readable attributes in the summary output, hand-edit ergonomics, and any auto-correction of legacy data the audit proves is safely fixable. |

## 3. Background: how it works today

### 3.1 The label format
`disc*-summary.txt` carries lines like:
```
AudioTrack[2]: Director's Commentary
```
- **Writer:** `Services/Admin/SummaryFileSerializer.cs` (`AudioTrack[{Index}]: {Name}`). Audio only.
- **Parser:** `TheDiscDb.Core/SummaryFileParser.cs` (regex `AudioTrack\[(\d+)\]`). Audio only.

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
- **No subtitle table exists** — subtitles are never captured in a contribution.
- **No type/language/codec is stored** on the contribution row; those come live from `disc*.txt`
  via `GetDiscLogs`, so the serializer already has the log at write time (it can emit decorative
  attributes *and* validate).
- `SubtitleTrack[n]` currently appears **only** as an aspirational comment in
  `TracksEdit.razor.cs`; nothing reads or writes it.

### 3.5 Why this is a problem
1. `[n]` is a re-derived **audio-only ordinal**, not the real MakeMKV stream index in `disc*.txt`.
2. Resolution is **position-only** — there is no identity check, so a wrong index silently
   mislabels a track.
3. **Subtitles cannot be labeled at all.**
4. It **diverges** from the DB / edit-suggestion model, which already keys a track by
   `(title index, full per-title MakeMKV stream index)` = `InputModels.Track.Index`.

## 4. Design

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

Suggested regex:
```
^(Audio|Subtitle)Track\[(\d+)\]:\s*([^#]*?)\s*(?:#.*)?$
```
→ group 1 = type, group 2 = index (int), group 3 = clean label.

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

### 4.4 Contribution storage (new)
- Add `UserContributionSubtitleTrack { Id, Index (int), Title (string), Item }`, mirroring
  `UserContributionAudioTrack`.
- Add `UserContributionDiscItem.SubtitleTracks` collection.
- EF Core migration to create the table. **No change** to `UserContributionAudioTrack` or its
  existing rows.

### 4.5 GraphQL
- Add `AddSubtitleTrackToItem(contributionId, discId, itemId, trackIndex, trackName)` mirroring
  `AddAudioTrackToItem` (upsert by `Index`).
- Regenerate the client schema/operation (`ContributionGraphQL`).

### 4.6 Contribution UI
- In `IdentifyDiscItems.razor.cs`, build a subtitle list from
  `title.Segments.Where(Type == "Subtitles")` (numbered `1..N`, same pattern as audio) and let the
  user label them; persist via `AddSubtitleTrackToItem`.
- Surface subtitle tracks in `DiscNamingItemDetail` (display + copy), symmetric to audio.

### 4.7 Serializer & parser
- `SummaryFileSerializer`: after the `AudioTrack[n]` lines, emit `SubtitleTrack[n]: {label}` for
  each labeled subtitle track. Optionally emit the decorative `# type · lang · codec` comment
  (see open questions).
- `SummaryFileParser`: parse `SubtitleTrack[n]` into a new `SubtitleTrackNames` collection on
  `DiscFileItem`; strip trailing ` #` comments from both audio and subtitle labels; **keep the
  existing `AudioTrack[n]` parsing unchanged** for back-compat.

### 4.8 Admin edit
- `TracksEdit` already treats audio + subtitle as describable. Fix the aspirational comment and
  ensure subtitle descriptions genuinely round-trip once the serializer/parser support exists.

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

- **Legacy correctness is unknown until Phase 0 runs.** The audit exists precisely to quantify it
  before any migration/correction is committed.
- **Same-language main-vs-commentary ambiguity** is inherent and cannot be resolved from logs;
  expect a residual set needing human ears.
- **Hard-fail imports** could surface pre-existing latent errors in the data repo (labels that
  were always wrong). That is desirable but may require a cleanup pass; the audit front-runs it.
- **DB→data-repo sync** for track `Description` edits (edit-suggestions) must be confirmed to
  persist and round-trip subtitle labels correctly once the format supports them.
- **Type-string normalization** (`Subtitle` vs `Subtitles`) must be handled consistently across
  importer, matching, and edit-suggestion code to avoid new mismatches.

## 7. Open questions

- Emit the decorative `# type · language · codec` comment in serializer output, or keep lines
  bare (`SubtitleTrack[n]: label`)? *(default: emit it, for hand-editing ergonomics.)*
- Exact heuristic set for "suspicious" audio labels in the audit (language-name detection,
  commentary keyword list, channel-count hints).
- Should the audit also verify **audio** labels created by the new contribution flow (expected
  100% clean) as a control group, to validate the heuristics?
- Where does the edit-suggestion sync persist track `Description` today (summary vs `discXX.json`),
  and does subtitle round-trip need work there?

## 8. Rollout / phases

- **Phase 0** — audit & validation report (read-only; no data changes).
- **Phase 1** — subtitle labeling end-to-end + strict import validation.
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
