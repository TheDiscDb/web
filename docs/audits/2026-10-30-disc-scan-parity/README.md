# Scanner rollout audit: all 491 saved comparisons

**Snapshot: 2026-10-30 UTC.** This repository bundle preserves the completed
audit's conclusions, typed machine-readable findings and input-hash provenance.
It is a historical baseline, not a live dashboard or evidence that later fixes
have shipped. Start with the assessment and prioritized recommendations below;
use the linked ledgers to revisit individual discs in future sessions.

For subsequent local fixes, verification and capture-dependent blockers, see
the separate [P0/P1 implementation follow-up](IMPLEMENTATION-STATUS.md).
The findings below remain the original audit snapshot.

Raw uploaded logs/manifests, contribution-metadata downloads, browser profiles
and authentication data are deliberately excluded. Reading the results does
not require the original Copilot session.

## Assessment

**Useful-content availability is substantially better than the dashboard's strict parity scores suggest, but broad scanner-default rollout is not yet justified.**

Across 3,339 contributor-identified useful choices, recorded metadata supports availability of **3,234 (96.86%)**. Another **105 choices on 21 discs** remain unresolved, acquisition-inconsistent, ambiguously represented, or affected by meaningful boundaries/timing. **406 of 427 discs with known useful labels** have all those choices available by this assessment.

These are not whole-corpus success rates or playback guarantees. There are **64 discs without useful reference labels**, **970 unresolved unclassified reference rows**, and substantive stream-labeling questions. Preserving a feature's clip sequence inside a longer container does not necessarily make that feature independently identifiable. Conversely, an extra one-second boundary fragment is not a lost feature.

Two actionable code defects are confirmed:

1. **Production Re-compare does not persist replacement comparison histories.** Its success count currently overstates successful updates.
2. **DVD audio-format codes are decoded incorrectly**, including DTS becoming Unknown and MPEG-1 being labeled MPEG-2.

Keep optional collection and log fallback. Fix measurement persistence first; then resolve high-impact navigation and semantic-labeling risks using fresh current-producer evidence before considering a tightly bounded pilot. Do not retire logs or silently replace the documented rollout gate.

## Scope, evidence and reproducibility

This is the completed **saved-artifact audit**, not a new physical-disc or playback audit. It covers every disc in the frozen 491-entry admin inventory, not merely mismatches or the ten planning samples.

| Evidence | Coverage |
|---|---:|
| Contribution-disc comparisons inventoried and locally evaluated | 491 / 491 |
| Original comparison/log/manifest assets | 1,473 / 1,473 |
| Original bytes, verified by individual lengths and SHA-256 receipts | 278,712,578 |
| Refreshed assets, independently downloaded and verified | 1,473 / 1,473 |
| Original/refreshed assets with identical SHA-256 | 1,473 / 1,473 |
| Contribution metadata retrieved | 490 / 491 |
| Identified item declarations | 3,437 |
| Useful declarations corroborated against published data | 3,141 |
| Raw log title rows assessed | 15,996 |
| Scanner candidates not selected as resolved counterparts, assessed separately | 2,271 |
| Producer versions represented | 14 |
| Newly regenerated scans or available navigation captures | 0 |

The original snapshot began on 2026-10-30 UTC; exact acquisition times and hashes are preserved in receipts. Published-reference indexing examined 6,156 canonical disc JSON files and matched 381 of 488 distinct metadata hashes. Metadata was not inferred by movie-name similarity.

Source baselines: web `6a46d65e615cb831f9394dde446d109b05018ee6`, tools `acf147b0e5641ceebf56acfd00bee8feb9ac6bfb`, data `768d2c50fdab2e25d3f66de9d084210569d96cbe`. Local comparison build: `1.0.0+6a46d65e615cb831f9394dde446d109b05018ee6`.

The 491 records contain 489 distinct manifest content hashes and 489 distinct expected hashes. These are contribution-disc counts, not 491 independently established physical-disc identities. Repeated manifest hashes occur for 2537/2538 (Taken 3 / 8 Mile, one acquisition quarantine) and 2742/2743 (the two Atlantis titles). Do not merge records by title or treat an inventory fingerprint as proof that a pasted log belongs to the scan.

No application fixes, repository-plan edits, commits, deployments, source-default changes or log removals were made. The sole production action was the authorized Re-compare after original evidence was secured.

### Measurement provenance

- **Saved:** original production histories, all with one comparison record.
- **Refreshed production:** the UI reported "Re-compared 491 discs," but every history and asset remained byte-identical. There is no persisted new deployed-comparer measurement to report.
- **Current local:** all frozen log/manifest pairs were evaluated in a separate console process using the existing OdmScanner/report APIs and current source. Reports retain build revision and input hashes.
- **Regenerated scanner:** not available. Re-comparing an old manifest does not run current navigation parsing or generation.

The per-disc result is therefore explicit even when evidence is insufficient. Unsupported cases and unknown usefulness have not been removed from the inventory.

## Strict parity and useful-content coverage answer different questions

### Saved versus current-local comparison

| Population | Discs | Saved technical matches | Current-local technical matches | Saved graded measured / mean | Current-local graded measured / mean |
|---|---:|---:|---:|---:|---:|
| All | 491 | 157 | 140 | 192 / 76.0% | 490 / 75.0% |
| Blu-ray | 303 | 71 | 63 | 108 / 71.1% | 303 / 73.5% |
| DVD | 101 | 52 | 52 | 50 / 75.7% | 101 / 77.6% |
| UHD Blu-ray | 86 | 34 | 25 | 34 / 91.9% | 86 / 77.1% |
| Unknown / DifferentDisc | 1 | 0 | 0 | 0 / not measured | 0 / not comparable |

Current-local technical results are 140 Match, 350 Mismatch and one DifferentDisc. Saved strict identify results were zero matches among 203 measured discs, with 288 not measured. Current-local strict identify results are 490 mismatches and one not comparable.

Saved graded coverage omitted 299 discs. Its 942 missing and 1,391 extra source-identity occurrences describe that measured subset, not missing useful programs. In particular, the UHD mean change is not evidence that a new scanner lost approximately 15 percentage points: the measured population changed from 34 to 86, and no new manifests were generated.

Keep the existing technical and strict/graded-v1 definitions intact. Graded v1 weights coverage/presentation/order/labeling 40/25/15/20, gives occurrences equal weight, and uses source identity for alignment. It is not a useful-feature recall metric.

### Known useful choices by format

| Format | Discs with useful labels / all discs | Useful choices | Availability supported | Unresolved choices | Discs with every known useful choice available |
|---|---:|---:|---:|---:|---:|
| Blu-ray | 271 / 303 | 2,604 | 2,515 | 89 | 255 / 271 |
| DVD | 71 / 101 | 367 | 360 | 7 | 69 / 71 |
| UHD Blu-ray | 85 / 86 | 368 | 359 | 9 | 82 / 85 |
| Unknown | 0 / 1 | 0 | 0 | 0 | Not evaluated |
| Total | 427 / 491 | 3,339 | 3,234 | 105 | 406 / 427 |

Useful declarations comprise 1,467 main-feature/episode choices and 1,872 other useful choices. The unresolved population contains **29 main-feature/episode declarations across 17 discs** and 76 other useful declarations. These are review/blocker signals, not 29 proven missing programs: they include mismatched acquisitions, angle ambiguity, unsupported metadata and retained cores with longer boundaries.

Another 98 explicitly named menus/logos/warnings or comparable incidental items are separately documented. Short duration alone never caused an item to be excluded.

### What "available" means here

| Useful-choice assessment | Choices | Interpretation |
|---|---:|---|
| AvailableRecordedEvidence | 3,154 | Recorded source/content evidence supports availability |
| AvailableWithSmallBoundaryFragments | 74 | Complete reference core retained; added fragments total at most 1.002 seconds |
| AvailableEquivalentCandidates | 4 | Equivalent recorded alternatives exist; no uniquely selected counterpart asserted |
| AvailableWithTruncatedReferenceMap | 2 | Explicitly truncated reference map agrees with full scanner prefix and other metadata |
| AcquisitionInconsistent | 45 | Do not infer scanner omissions from an untrustworthy pairing |
| AnglePerspectiveUnresolved | 21 | A meaningful alternate presentation has not been established |
| ContentPresentWithAdditionalSegments | 24 | Core present, but meaningful extra material/boundaries prevent an availability pass |
| NeedsFurtherEvidence | 15 | Independent choice, metadata or timing remains unresolved |

Matching first respects exact identity. Blu-ray/UHD aliasing requires the same ordered segment map, equal positive byte size and displayed duration within one second. Refinement checks full file/clip sequence evidence and alternatives across scanner choices. DVD title-number aliases are not guessed from cell maps. Small-fragment equivalence concerns added boundaries, not a minimum useful-item duration.

This establishes metadata-supported availability, not equivalent playback, authored in/out points, selected angles, commentary tracks or complete semantic stream fidelity. Chapter/size differences remain inspectable even where item availability is supported.

### Why raw matching can seriously mislead

**3156, Good Will Hunting:** 45 log rows, 48 scanner rows, only six exact keys aligned, graded-v1 similarity approximately **5.286%**. Nevertheless, all seven known useful choices have metadata-supported scanner counterparts. Many differences are playlist-versus-stream aliases; four other identified entries are incidental warnings/logos/menu items.

**2776/2777, The Haunting of Bly Manor:** same-key log parts misleadingly map to long PlayAll containers. Alternative scanner episode playlists retain the individual episodes with approximately one-second tails. Searching only the same key would falsely report lost episodes.

## Complete unresolved-useful ledger

The following 21 discs account for all 105 unresolved useful declarations. An item-level ledger records source, label, reference/scan evidence and assessment; this table is not a claim that every listed item is absent.

| Disc | Title | Choices | Reason for review |
|---|---|---:|---|
| 2538 | 8 Mile | 4 | Acquisition inconsistency |
| 2562 | What We Do in the Shadows | 41 | Acquisition inconsistency |
| 2584 | The Hunted | 1 | Retained core with additional material |
| 2586 | Heist | 1 | Retained core with additional material |
| 2626 | Blade Runner 2049 | 5 | Alternate perspective unresolved |
| 2627 | Blade Runner 2049 | 3 | Alternate perspective unresolved |
| 2638 | Tequila Sunrise | 1 | Retained core with additional material |
| 2642 | DreamWorks Dragons | 5 | Incomplete/unsupported DVD metadata |
| 2648 | There Will Be Blood | 3 | Combined extra parts and timing discrepancy |
| 2653 | Going in Style | 2 | Trailer cell-map difference; unrepresented previews choice |
| 2655 | Enchanted | 23 | Interactive clip boundaries and alternate perspectives |
| 2657 | Curse of the Pink Panther | 1 | Retained core with additional material |
| 2664 | Hellbound: Hellraiser II | 4 | Boundaries and further evidence required |
| 2684 | Red Dust | 1 | Retained core with additional material |
| 2689 | Dance, Fools, Dance | 1 | Retained core with additional material |
| 2690 | Marie: A True Story | 1 | Retained core with additional material |
| 2711 | Safe in Hell | 1 | Retained core with additional material |
| 2741 | Stargate | 2 | Theatrical/extended timing discrepancies |
| 2900 | Audition | 3 | Movie/commentary timing discrepancies |
| 2921 | Halloween | 1 | Retained core with additional material |
| 3026 | Narrow Margin | 1 | Retained core with additional material |

There are 19 ordinary known-useful review discs plus two acquisition-inconsistent discs. The separate DifferentDisc case 2541 has no matching useful-label inventory.

## Unidentified content and scanner-only candidates

Of 15,996 reference rows, 12,559 have no contributor usefulness label. All received an evidence assessment; they have not been assumed incidental. Overall, 1,075 reference rows remain unresolved, including the 105 useful declarations and **970 unclassified rows**.

| Raw-row cluster | Rows | Discs |
|---|---:|---:|
| Core sequence present, meaningful boundary review | 511 | 55 |
| DVD unsupported/incomplete metadata | 235 | 29 |
| DVD navigation/boundary difference | 168 | 6 |
| Acquisition inconsistent | 90 | 2 |
| Angle/perspective unresolved | 24 | 4 |
| DifferentDisc | 19 | 1 |
| Material timing discrepancy | 17 | 14 |
| Unrepresented Blu-ray/UHD reference choice | 9 | 8 |
| Unrepresented DVD reference choice | 1 | 1 |
| Grouping/incomplete evidence | 1 | 1 |
| Total unresolved | 1,075 | Overlapping disc populations |

For triage only, the 970 unclassified unresolved rows comprise 378 at 0-10 seconds, 214 at over 10 through 60 seconds, 122 at over 60 through 300 seconds, and 256 above 300 seconds. This does not certify the short rows as junk or the long rows as distinct useful programs.

The 2,271 scanner candidates not selected as resolved counterparts span 191 discs: 74 are equivalent recorded candidates, 115 are container/unresolved counterparts, and **2,082 remain unclassified scanner candidates**. They are not 2,271 useless extras. Distinct content, meaningful alternate cuts and excessive duplicate noise require different treatment.

## Important content/navigation cases

### Quarantine acquisitions before changing scanner heuristics

- **2541:** DifferentDisc; contribution identity and scan do not agree.
- **2538, 8 Mile:** 35 consistency warnings; important log playlist sources are absent from the scan inventory. Four useful declarations are affected.
- **2562, What We Do in the Shadows:** 50 source/size consistency warnings; 41 useful declarations are affected.

The last two account for 45 of 105 unresolved useful choices. The legacy content-hash check compares a manifest with its contribution record, not independently with the pasted MakeMKV log. Re-pair or recollect these cases; do not make a generator rule compensate for mismatched evidence.

### Distinguish retained content from usable independent choices

- **2584 The Hunted, 2657 Curse of the Pink Panther, 2921 Halloween, 3026 Narrow Margin:** main cores remain with roughly 11-34 seconds of additional material and changed historical split identities.
- **2648 There Will Be Blood:** separately identified extras Part 1 and Part 2 become combined; "The Story of Petroleum" also has an eight-second timing discrepancy.
- **2655 Enchanted:** independent interactive clips and alternate perspectives are not resolved by finding a larger container.
- **2626/2627 Blade Runner 2049:** do not flatten alternate clip/angle maps into one guessed presentation.

**2671 Gladiator** has same-key comparisons of 22:28 versus 2:14 (`00073.mpls`), 13:03 versus 1:00 (`00075.mpls`), and 10:04 versus 1:13 (`00082.mpls`). The complete corresponding reference clip sequences appear inside scanner `00000.mpls` (2:50:56). **2813 The Lion King** similarly has 17:57 versus 4:08 and 12:38 versus 3:03, with complete sequences inside another 1:28:23 container. These are serious independent-choice/timing questions, not proof the disc's content bytes disappeared.

Other material timing candidates include 2523 Licorice Pizza (-130 seconds), 2535 Parenthood (-100), 2807/2809 Neon Genesis Evangelion (-78/-221), 2875 The Thing (-127), 2985 Rudolph (-123), and 3061 TAR (-635). Identified Stargate cuts differ by 10/17 seconds despite matching maps/sizes; Audition's movie and two commentary variants differ by ten seconds each. These require authored timing/capture evidence, not a blanket duration tolerance.

### Current-producer complex navigation remains a real uncertainty

**3168, All Creatures Great & Small** uses the newest saved producer. It has 382 log rows, 378 scanner rows and 201 reported field-difference entries; the raw audit leaves 206 rows unresolved, 204 with their core sequences located alongside additional material.

One 33:14 reference becomes 52:10: the same ten core clips plus clip `00002`, lasting 1,135.092289 seconds. Stored diagnostics explicitly report **"Split not evaluated: subpaths."** This is consistent with the safety exclusion in `BluRayPlaylistRules.SplitIncompatiblePlayItems`.

There are no useful identifications on this disc. Consequently, this does not establish that 206 episodes were damaged, or that the shorter reference is always the intended presentation. It does establish that current-producer complex-authoring equivalence cannot be waved away as historical noise. Capture the authored playlist/CLPI context and identify actual useful presentations before changing the guard.

### DVD uncertainty is partly historical and partly unsupported

**2642, DreamWorks Dragons:** four identified episodes and PlayAll have null duration, zero projected size and empty maps in an old `0ef3ec4...` manifest. The playback-flag bug was subsequently corrected in `5850228` / PR #111: the multi/random-PGC bit is `0x40`, not `0x02`. A fresh capture/scan is necessary to distinguish an already-fixed historical case from genuinely unsupported navigation.

True multi/random-PGC and multi-angle timing remain explicitly unsupported; guessed durations are not a fix. **2653, Going in Style** has a same-duration Wonder Woman trailer with a cell-map discrepancy and an unrepresented 6:46 previews choice. It remains a substantive selection case.

### Lower priority: tiny boundaries and cosmetic parity

Examples include 2516 The Monkey, 2542 Interview with the Vampire, 2978-2981 All Creatures Great & Small, and extras on 2611 In the Mouth of Madness and 2863 Falling Down. Complete reference cores plus approximately one-second terminal fragments are much less important than useful choices that cannot be independently identified.

Current source already permits one-second/1.001-second tail handling for ordinary sequential presentations, while retaining a stricter half-second guard for complex authoring. Do not propose this existing generator work as a new fix or claim it has been validated against regenerated versions of these saved scans.

## Semantic streams and saved-label safety

Title availability does not establish that every language, commentary or accessibility choice is properly represented.

- Audio counts differ on 946 known useful items across 277 discs.
- Subtitle counts differ on 2,265 across 376.
- Such counts overstate semantic loss when MakeMKV exposes a DTS core alongside DTS-HD, or paired PGS/forced-only representations. Do not fabricate extra streams to mimic these rows.
- Existing identified contributions remain **log-first**, including in Default mode. This protects their saved labels; this audit does not establish existing data corruption.

### Ordinals are not cross-source identities

The audit assessed all 613 named saved track labels:

| Assessment | Labels | Items | Discs |
|---|---:|---:|---:|
| Ordinal in range, not semantic proof | 199 | 156 | 29 |
| Ordinal rebinding needs review | 183 | 101 | 65 |
| Valid log ordinal would be out of range in scan | 226 | 125 | 76 |
| Mapping unmeasured | 5 | 2 | 2 |

Item/disc populations overlap; do not add them as unique totals. Per-type ordinals are 1-based. On **2515 Doctor Who / Castrovalva**, six log audio rows become three scanner rows; commentary/isolated-score ordinals 3-6 cannot simply be reused. This is not proof the commentary audio payload is absent.

Native manifests carry richer stream identity than the import/projection retains. A safe migration needs explicit source identity and semantic correspondence, not index reuse or count equality.

### Captions and language codes

The log reports Closed Captions on 95 useful items across 30 discs without corresponding projected caption capability. The native generator already records `Line21ClosedCaptionFields`; **3155 Soap** contains `[1]`, but the import stream model/mapper drops that information. Preserve this as declared caption evidence, not a fabricated subtitle stream or proof of decodable payload.

The analysis normalizes two-letter DVD ISO-639 codes and bibliographic/terminologic aliases, such as `en/eng`, `fre/fra` and `ger/deu`. Most initial audio-language gaps disappeared after this correction. The generator's current normalization only trims and lowercases.

Remaining signals include Japanese subtitle metadata on 2614, 2904, 2754, 3024 and 3025; English caption-only gaps; Enchanted selections; and the quarantined acquisition. These are metadata/capability signals, not proof payload is absent. Exact rows are retained in `final-summary.json` under `languageSignals`.

### Confirmed DVD audio-code defect

`DvdIfoParser.ParseAudioStreamsAsync`, `web\code\TheDiscDb.OpticalDiscParsers\Dvd\DvdIfoParser.cs:647-660`, currently maps numeric codes 1/2/3/4/5 to MPEG-1/MPEG-2/LPCM/DTS/SDDS.

Independent libdvdread decoding corroborates **0 = AC-3, 2 = MPEG-1, 3 = MPEG-2 extension, 4 = LPCM, 6 = DTS**. Reserved values must not be assigned the displaced codecs. This audit does not independently establish SDDS code 7.

The frozen corpus demonstrates four DTS-to-Unknown pairs on 2533 The Sting and 2750 Atlantis: Milo's Return, plus three MPEG-layer-2-to-MPEG-2 pairs on 2889/2890/2891 Ekkehard: **seven aligned audio pairs across five discs**. The error is a decoder-table defect, not merely alternate spelling.

Independent sources: [libdvdread ifo_print.c](https://raw.githubusercontent.com/xbmc/libdvdread/master/src/ifo_print.c) and [ifo_types.h](https://raw.githubusercontent.com/xbmc/libdvdread/master/src/dvdread/ifo_types.h). The existing codec-name smoke test does not independently validate raw numeric codes.

## Prioritized fixes and validation

Priorities describe proposed work, not implemented fixes. Affected historical counts are exposure evidence, not promised improvements from a hypothetical patch.

### P0 - Make comparison updates persist and report truthful outcomes

**Confidence: confirmed. Capture required: no. Owner: storage/comparison service.**

- Evidence: all 491 authorized re-comparisons were reported successful, but all 491 comparison histories remained byte-identical and single-record.
- Cause: `DiscScanComparisonStore.AppendAsync` (`web\code\TheDiscDb\Services\DiscScanComparisonStore.cs:81-96`) updates an existing history via `assetStore.Save`. Both overloads in `web\code\TheDiscDb.Data.Import\BlobStorageStaticAssetStore.cs:68-105` intentionally skip existing blobs. `DiscScanComparisonService.cs:88-117` counts returned computations, not verified persisted updates.
- Minimum change: introduce an explicit mutable-history replacement/upsert contract; retain create-only Save semantics for unrelated callers. Do not delete the old history before replacement. Handle concurrent appends with conditional/ETag conflict handling, and distinguish attempted, computed and persisted outcomes.
- Provenance: store comparer build and input hashes for each run, so histories are interpretable even after assets change.
- Validation: append twice through realistic storage semantics, read back two distinct runs newest-first, test the 20-record cap, failure reporting and concurrent update conflict handling. Existing serialization tests alone are insufficient.
- Risk: a global overwrite change could alter immutable asset behavior; an unconditional read-modify-write replacement could lose concurrent history. Keep the change local and explicit.

### P1 - Correct DVD audio-format decoding

**Confidence: confirmed. Capture required: no for the numeric mapping. Owner: DVD parser.**

- Exposure: seven observed disagreements across five DVD records; other code values may be affected even where this corpus does not exercise them.
- Minimum change: correct `DvdIfoParser.cs:647-660` using independently documented numeric meanings and preserve unknown/reserved values honestly.
- Validation: table-driven synthetic IFO attributes with raw codes 0-7, independently specified expected supported/reserved outcomes, and channel/language fields held constant. Extend the existing parser tests rather than merely asserting accepted codec names.
- Risk: codec label changes affect downstream matching; numeric correction should not change stream order or manufacture streams. Do not claim a seven-pair corpus fix until rerun against regenerated metadata.

### P1 - Add versioned usefulness and semantic-capability reporting

**Confidence: confirmed metric mismatch; content-equivalence rules require conservative validation. Capture required: not for basic reporting, yes for unresolved navigation. Owner: comparison/dashboard.**

- Evidence: Good Will Hunting's approximately 5.286% v1 score despite seven supported useful choices; 74 useful choices affected only by tiny tails; 64 discs without useful labels.
- Source: `web\code\TheDiscDb.Contributions\OpticalDiscManifest\IdentifySimilarityComparer.cs`, `DiscLogManifestComparer.cs`, and admin aggregation/presentation.
- Minimum change: add a separate versioned report for known useful choice coverage, all-known-useful disc coverage, required-choice blockers, semantic capability, unknown content and acquisition quality. Keep strict/graded-v1 unchanged.
- Alignment: record exact/alias/boundary/angle evidence separately; normalize ISO languages; show metadata-supported equivalence as such. Avoid content matches based solely on duration, names or aggregate size.
- Validation: Good Will Hunting alias case; Bly Manor alternative episode counterpart; duplicate occurrences; meaningful angle variants; short identified extras; combined-part negative case; DifferentDisc and missing-label denominators; ISO alias cases.
- Risk: a superficially higher blended score can hide lost episodes, useful extras or accessibility. Do not use this audit's 96.86% as an automatic rollout pass.

### P1 - Preserve semantic stream identity and lock saved label sources

**Confidence: confirmed projection/ordinal risk, not proven payload loss. Capture required: representative real cases for end-to-end equivalence. Owner: manifest import, mapping, identify UI.**

- Evidence: 226 named labels would fall outside scanner ordinals on 76 discs if blindly rebound; 183 more need ordinal-rebinding review. Existing log-first behavior is protective.
- Source: `web\code\TheDiscDb.Contributions\OpticalDiscManifest\OpticalDiscManifestDocument.cs:145-163`, `OpticalDiscManifestMapper.cs:85-145`; native `ManifestModels.cs:150-200`; `DiscLogSourceSelector.cs`; `IdentifyDiscItems.razor.cs:414-499`.
- Minimum change: retain relevant native stream identifiers/coding/category metadata and explicit source provenance. Define semantic correspondence before any migration; preserve log-first locking until a reviewed mapping exists. Normalize language codes at the appropriate comparison/presentation boundary.
- Validation: Doctor Who commentary/isolated score, DTS-HD/core, PGS/forced-only, alternate-language and accessibility cases; prove saved labels do not silently move when sources change.
- Risk: PID alone is not universal identity, and a codec/language pair is not enough to distinguish two commentaries. Never infer absence solely from row-count reduction.

### P1 - Diagnose independent-choice boundaries, authored timing and complex navigation

**Confidence: observed representation differences confirmed; generator remedy not established. Capture required: yes. Owner: Blu-ray generation/reconciliation and DVD navigation.**

- Evidence: 24 identified useful choices retain cores with meaningful additions; 21 angle/perspective choices are unresolved. Gladiator/Lion King container evidence and current-producer 3168 merit priority even without useful labels.
- Source: `web\code\TheDiscDb.OpticalDiscManifest\Generation\BluRayPlaylistRules.cs:37-116,275-335`, `BluRayTitleMapper.cs:9-120`, `BluRayTitleReconciler.cs`, and `DvdManifestBuilder.cs:124-125,242-250`.
- Minimum next change: first add sanitized navigation replay fixtures covering 3168 subpaths, authored in/out ranges, independent extras, interactive perspectives, and the problematic DVD cases. Then change only rules supported by those fixtures. Expose useful independent parts only where authoring evidence supports them.
- Validation: actual useful presentations on 3168; Gladiator/Lion King standalone choices; There Will Be Blood parts; Enchanted/Blade Runner alternate presentations; Stargate/Audition timing; DreamWorks Dragons against the already-corrected DVD flag parser; Going in Style previews.
- Risk: disabling the subpath/angle guard, splitting every clip, using full clip duration for every authored range, or enlarging duration tolerances can corrupt legitimate presentations. Do not prescribe any of those blanket changes.

### P1/P2 - Preserve declared caption capability honestly

**Confidence: confirmed projection omission. Capture required: no for field preservation; playback evidence for stronger claims. Owner: import model, mapper, identify UI.**

- Exposure: 95 known useful caption signals across 30 discs; newest-producer Soap is a concrete example.
- Source: native `ManifestModels.cs:150-200`, `DvdManifestBuilder.cs:465-501`, import `OpticalDiscManifestDocument.cs:145-163`, and `OpticalDiscManifestMapper.cs`.
- Minimum change: preserve Line 21 declarations through import and expose an explicitly declared caption capability where useful for identification, without inventing subtitle rows.
- Validation: Soap/native flag round-trip, absent flags, preserved labeling behavior and no double-counted subtitle streams.
- Risk: IFO declarations need not prove actual caption payload or playback support. Accessibility warrants high priority, but the UI must not overpromise.

### Operational prerequisite - Repair/recollect invalid pairs

Quarantine 2541, 2538 and 2562 from scanner-defect estimates while keeping them in acquisition-quality reporting. Check contribution identity, log source inventory and scan inventory before accepting a pair as evidence. Verify repeated inventory hashes rather than merging them by movie name.

Recollect 2642 with current DVD parsing and collect navigation captures for 3168 and the prioritized timing/boundary cases. Current saved-manifest comparison cannot validate generator repairs.

### P2 - Presentation noise and low-impact parity

Only after useful choices and label safety: improve proven alias display, numeric padding, row ordering, redundant choices and tiny tails. Use the existing guarded tail logic rather than another unsupported heuristic. Establish whether extra scanner rows are genuinely redundant before suppressing them.

## Rollout decision and evidence required

### The existing gate is not met

`web\docs\FEATURE-DISC-SCAN-ROLLOUT.md:187-193` requires the **current producer**, at least 200 compared discs per format, at least two browsers and at least 98% exact match per format, with every residual mismatch explained.

The entire historical corpus has only 101 DVDs and 86 UHDs. The newest saved producer `1.0.0+5d10f8f...` has **seven discs total**, not a qualifying sample:

| Disc | Title | Format / browser | Known useful availability | Unresolved raw rows |
|---|---|---|---:|---:|
| 3150 | Brave | Blu-ray / Safari | 1 / 1 | 0 |
| 3151 | Spotlight | UHD / Chrome | 1 / 1 | 0 |
| 3152 | Spotlight | Blu-ray / Chrome | 4 / 4 | 0 |
| 3153 | How I Met Your Mother | DVD / Edge | No useful labels | 2 |
| 3155 | Soap | DVD / Opera | 10 / 10 | 0 |
| 3156 | Good Will Hunting | Blu-ray / Chrome | 7 / 7 | 1 |
| 3168 | All Creatures Great & Small | Blu-ray / Chrome | No useful labels | 206 |

Its 23 known useful choices are supported, but caption limitations and unclassified complex-navigation risks remain. Four Blu-rays, two DVDs and one UHD cannot establish format readiness.

The whole corpus spans Chrome 157, Edge 150, Firefox 87, Safari 91 and Opera six; MakeMKV versions 1.18.3 (207), 1.18.4 (154) and 2.0.0 (130). Format/producer/browser/MakeMKV cross-cohorts are supplied separately. These are confounded historical populations, not controlled browser or version experiments.

### Recommendation

**Continue optional scanner collection; no broad Default switch and no log retirement.** After fixing persistence, consider only an explicitly gated pilot for demonstrated-supported cohorts with fresh evidence and immediate log fallback. A format-only whitelist is insufficient: complex authoring and semantic-stream needs cut across formats.

A separate usefulness-based rollout policy would be more aligned with the user's objective than exact MakeMKV parity, but requires explicit agreement. Proposed acceptance dimensions, not an already-approved replacement gate:

1. Fresh, correctly paired scans from the intended producer across supported formats, browsers and representative authoring styles; record producer and comparer versions separately.
2. No unexplained missing/truncated required feature or episode in the eligible pilot, and reviewed independent access to useful extras/alternate cuts.
3. Explicit item and complete-disc useful-coverage denominators, with unknown usefulness and unsupported navigation shown separately; agree numeric thresholds and sample sizes before enabling defaults.
4. Demonstrated commentary/language/accessibility identification and saved-label source stability, not merely matching stream counts.
5. Proven persisted measurements, actionable failure diagnostics, working log fallback and a reversible configuration-based rollback.

Keep safety guards and existing log-first behavior. Exclude unsupported/uncertain cases from pilot eligibility without making them disappear from overall coverage reporting. Verify source-selection stability through new, resumed and already-identified contributions.

## Evidence files and reproduction

Links below are relative to this repository bundle. JSON preserves nested stream
signals and candidate/source lists that the original CSV exports omitted. Only
the authoritative refined results are included, not superseded first-pass lists.

| Artifact | Purpose |
|---|---|
| [Final summary](findings/final-summary.json) | Reconciled authoritative metrics and validation outcome |
| [All discs](findings/refined-discs.json) | One assessment for every inventory disc |
| [Useful/identified items](findings/useful-item-assessments.json) | All 3,437 declarations, including nested stream signals and alternative sources |
| [Unresolved useful items](findings/unresolved-useful-items.json) | Complete 105-choice review ledger |
| [All log rows](findings/all-log-assessments.json) | All 15,996 reference rows, including unidentified content |
| [Unresolved reference rows](findings/unresolved-reference-rows.json) | All 1,075 unresolved raw rows |
| [Scanner candidates](findings/scanner-candidate-assessments.json) | All 2,271 unselected/unresolved scanner candidates |
| [Named stream labels](findings/stream-label-audit.json) | Ordinal safety/capability evidence for 613 labels |
| [DVD codec disagreements](findings/dvd-codec-disagreements.json) | Seven demonstrated codec disagreements |
| [Cohorts](findings/cohorts.json) | Format x producer x browser x MakeMKV version |
| [Inventory](provenance/inventory.json) | Frozen website membership/detail paths |
| [Original receipts](provenance/original-receipts.jsonl) / [refreshed receipts](provenance/refreshed-receipts.jsonl) | Retrieval outcomes, times, lengths and SHA-256 for the excluded raw assets |
| [Comparison inputs](provenance/comparison-inputs.csv) | Per-disc comparer/tool versions, saved producer, input hashes and comparison time |
| [Re-compare outcome](provenance/recompare-outcome.json) | Recorded operation times, UI success message and independently observed unchanged histories |

Each JSON ledger is an array except `final-summary.json`, which is an aggregate
object. `discId` joins the inventory, per-disc findings, item/row ledgers and
comparison provenance. Use `source`, `referenceChoiceKey` and `logRow`/`row` to
trace individual choices; do not assume one source key represents only one
occurrence or perspective. Historical `publishedPaths` refer to the sibling
data checkout used by the audit, not files included in this bundle.

Source citations retain the sibling-workspace prefix `web\`; drop that prefix
when navigating inside this repository. The cited baseline commits distinguish
the audit's source from subsequent implementation work.

The original raw corpus, full native comparison reports, projections and
session-specific acquisition/analysis scripts are not included. Acquisition
receipts identify those original files without embedding them. To rerun rather
than revisit this audit, obtain authorized inputs, check their SHA-256 values
against the receipts, and use the existing OdmScanner/report APIs at the recorded
revisions. Current website assets may have changed; hash differences require a
new snapshot, not an overwrite of this historical assessment. Regenerating
scanner output additionally requires fresh disc/navigation captures, which this
audit did not have.

**Completion boundary:** every frozen disc, identified useful declaration, raw reference row and unselected scanner candidate has an assessment or explicit evidence-gap classification. Missing captures and unknown usefulness remain open evidence needs, not hidden passes. This report recommends fixes; it does not claim those fixes have been applied or validated on freshly generated scans.
