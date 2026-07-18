# Feature Spec: Disc Mapping Auto-Suggestions

## 1. Problem Statement
When users upload MakeMKV log files (`disc*.txt`) for a new disc, manually mapping the layout of the disc to useful items (like episodes, main movies, commentary tracks, featurettes) can be tedious. However, historical data shows distinct structural patterns based on the type of media (Movie, Series, etc.) and format.

We can analyze the layout of newly uploaded discs and surface **Auto-Suggestions** to the user to streamline the mapping process.

## 2. Identified Patterns from Historical Data

### Movies
Based on an analysis of ~2,325 movie discs:
- **Main Movie Identification**:
  - In **83.2%** of cases, the `MainMovie` is the title with the largest file size (`Size`).
  - In **27.4%** of cases, the `MainMovie` uses `800.mpls` (or Segment Map `800`).
- **Commentary Tracks**:
  - In **67.9%** of cases where a commentary track exists, it is the **last** English Stereo track on the title.
- **Featurettes**:
  - **95.6%** of Featurettes contain **no foreign audio tracks**. They are almost exclusively English-only (or native language-only).

### Series / TV Shows
Based on an analysis of ~1,983 series discs with multiple episodes:
- **Episode Grouping (Size & Length)**:
  - In **74.0%** of discs, the episodes are within **15% file size** of each other.
  - In **74.0%** of discs, the episodes are within **5 minutes** of length of each other.
- **Episode Ordering**:
  - **54.7%** of the time, episodes are ordered chronologically by `SourceFile` (e.g., `00001.m2ts` -> `00002.m2ts`).
  - **36.4%** of the time (specifically common on Blu-rays), episodes are ordered chronologically by `SegmentMap` / playlist number.

### 3. Proposed Auto-Suggestion Heuristics

When a user uploads a new disc, we will run the data through a rules engine to generate mapping suggestions:

#### Rule 1: The "Main Movie" Guesser
- **Trigger**: The release is categorized as a "Movie".
- **Logic**: Find the title with the largest `Size`. If its duration is > 60 minutes, tag it with suggestion `Type: MainMovie`.
- **Confidence Boost**: If the source file is `00800.mpls`, increase confidence to High.

#### Rule 2: The "Series Episodes" Guesser
- **Trigger**: The release is categorized as a "Series".
- **Logic**: Find groups of 3+ titles that have similar durations (within 5 minutes) and similar sizes (within 15%). 
- **Action**: Tag the group as `Type: Episode`.
- **Sorting**: Suggest an episode order by sorting first by `SourceFile`, then falling back to `SegmentMap`.

#### Rule 3: The "Commentary Track" Guesser
- **Trigger**: A title has been identified as a `MainMovie` or `Episode`.
- **Logic**: Look at the audio tracks. If there are multiple English tracks, and the final English track is `Stereo` (2.0), suggest it is a `Commentary Track`.

#### Rule 4: The "Featurette" Guesser
- **Trigger**: Remaining unmapped titles on a Movie or Series disc.
- **Logic**: If the title duration is between 2 minutes and 45 minutes, AND it only contains English audio, suggest `Type: Featurette`.

#### Rule 5: The "Noise" Filter (Hide by default)
Based on an analysis of over 150,000 unmapped (noise) titles across the data:
- **68.3%** of all noise titles have **no audio tracks** at all (compared to only 0.9% of mapped useful titles).
- **3.8%** are identical duplicates of a mapped item (exact same file size or segment map).
- **Trigger**: Unmapped titles.
- **Logic**: Tag a title as **Noise** and collapse/hide it in the UI by default if:
  1. It contains **zero audio tracks**.
  2. Or, its `Size` or `SegmentMap` exactly matches a title we have already suggested a mapping for.

#### Rule 6: The "Likely Useful" Confidence Boost
While **89.0%** of all noise titles are under 2 minutes long, short runtimes can still be useful (e.g., short extras, trailers, or TV spots). Instead of strictly filtering these out as noise:
- **Trigger**: Any unmapped title with audio.
- **Logic**: If a title is **over 2 minutes long**, apply a confidence boost that it is a useful item. Titles under 2 minutes will not be hidden or marked as noise automatically; they simply will not receive this positive confidence boost unless they match a different heuristic.

## 4. Third-Party Integrations (Future Enhancement)

### DVDCompare Integration
To boost confidence in these heuristics, we can integrate with databases like `dvdcompare.net`.
- **Workflow**:
  1. Scrape or query DVDCompare for the specific movie/edition.
  2. Extract the runtimes of the listed extras (e.g., "Making of" - 12:34).
  3. Match those runtimes against the parsed MakeMKV titles (e.g., an unmapped title with duration 12:34).
  4. Suggest the specific name of the featurette.

## 5. User Experience (UX) Flow
1. **Upload & Parse**: User uploads the MakeMKV log files. The server parses them into raw titles/tracks and runs the Auto-Suggestion Engine.
2. **Disc Identify Page**: The user is taken to the mapping screen. Instead of a blank slate, the UI surfaces suggestions generated by the rules engine.
3. **Actionable Suggestions (High Confidence)**: 
   - For items we can identify definitively (like `MainMovie` or specific TV `Episodes`), the UI displays a concrete mapping suggestion.
   - Users can accept or reject these suggestions with a single click per item.
   - A global "Bulk Accept" and "Bulk Reject" option is available for these high-confidence guesses.
4. **Categorical Suggestions (Featurettes)**:
   - For items we suspect are `Featurette`s, we cannot suggest an exact title name. 
   - Instead of an accept/reject button, these rows will be made **visually distinct** (e.g., a specific background color, border, or a "Likely Extra" badge) to prompt the user to investigate and name them.
5. **UI Filters for Relevancy**:
   - To reduce the overwhelming number of tracks, the page filters will include a "Relevant Items Only" toggle.
   - This filter hides identified noise (like 0-audio tracks or duplicates) so the user can focus purely on mapping the actual content.
6. **Smart TV Show Episode Mapping**:
   - The engine flags a grouped set of tracks as "Likely Episodes" based on similar size/length heuristics.
   - When the user clicks to identify these episodes, the UI prompts them to select the episode range (since the system already has the full episode list for the series).
   - Once the range is selected, the engine auto-fills the exact Season/Episode numbers and Episode Titles sequentially down the list of flagged tracks, utilizing `SourceFile` or `SegmentMap` chronological ordering.