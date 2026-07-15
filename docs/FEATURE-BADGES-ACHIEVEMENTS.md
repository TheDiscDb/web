# Feature Spec: Badges & Achievements

## 1. Summary

Add a gamification layer to TheDiscDb that rewards contributors with **badges /
achievements** for meaningful actions and milestones, groups earned progress into
**levels**, and surfaces both on the existing public contributor pages. The goal is to
recognize and motivate high-quality contribution without incentivizing low-value work —
achievements that could otherwise be gamed are gated on the outcomes of the existing
review workflow (i.e. work that was actually *approved / imported*), so quantity never
beats quality.

## 2. Status

🔵 **Proposed** — no implementation started.

| Phase | Scope |
|-------|-------|
| **Phase 1** | Everything except the dedicated browse page: data model, launch backfill, the full achievement catalog, auto-derived levels, and display of badges/level/progress on the `/contributedby/{username}` public profile and the `/leaderboard`. |
| **Phase 2** | A dedicated "all achievements" browse & progress page — see every available achievement, which are earned vs locked, and progress toward un-earned ones. |

## 3. User stories / scenarios

- **As a contributor**, I want to earn a badge when my first contribution is imported,
  so I feel recognized for getting started.
- **As a contributor**, I want to see badges and my level on my public profile
  (`/contributedby/{username}`), so my track record is visible to the community.
- **As a visitor / community member**, I want to see contributors' levels/badges on the
  leaderboard, so I can recognize prolific and trusted contributors.
- **As a returning contributor**, I want past work to already count when the feature
  launches, so I'm not starting from zero.
- **As the site owner**, I want achievements tied to *approved / imported* work so the
  system rewards quality and doesn't encourage noise or low-value edits.
- **As a contributor (Phase 2)**, I want a page listing all achievements with my progress
  toward the ones I haven't earned, so I know what to aim for.

## 4. Scope

### In scope (Phase 1)
- Data model for achievements, user-earned achievements, points, and levels.
- A defined **achievement catalog** (see §5.4) with triggers and thresholds.
- **Retroactive backfill** of earned achievements/levels from existing history on launch.
- **Hybrid computation**: event-driven awarding on relevant actions + a nightly
  reconciliation job to catch misses.
- **Auto-derived levels** from accumulated points.
- **Display** of badges, level, and progress on `/contributedby/{username}`, and of
  level/badges on `/leaderboard` rows.
- **Audit logging** of award/revoke events.

### In scope (Phase 2)
- Dedicated achievements **browse & progress page** (earned vs locked, progress bars).

### Out of scope (for now)
- User-configurable badge visibility / privacy toggles.
- Awarding based on non-contribution activity (comments, logins, etc.).
- Real-money or external rewards.
- Leaderboards *ranked by* achievement points (leaderboard stays release-count based;
  badges are decoration on it).

## 5. Design

### 5.1 Identity model (critical context)

TheDiscDb has **two identity concepts** that this feature must bridge:

- **Authenticated site users** — `TheDiscDbUser : IdentityUser`. Sign-in is **GitHub
  OAuth** (`AspNet.Security.OAuth.GitHub`). These users own `UserContribution.UserId` and
  `EditSuggestion.UserId`. This is where *actions* originate.
- **Public contributor attribution** — the `Contributor` entity (keyed by **GitHub
  username** in `Contributor.Name`) with a many-to-many to published `Releases`. The
  `/leaderboard` ranks contributors by `Releases.Count`; `/contributedby/{username}` lists
  a contributor's published releases. This is where *public identity / display* lives.

Because sign-in is GitHub-based ("a GitHub account for sign-in **and attribution**"), the
**GitHub username is the natural bridge** between the authenticated account and the public
`Contributor.Name`. The public profile surface for badges is therefore
`/contributedby/{username}`.

> **Design item to confirm during implementation**: the exact mapping between the
> authenticated account and the GitHub username / `Contributor.Name` — i.e. whether
> `IdentityUser.UserName` holds the GitHub login, or whether it must be derived from the
> external-login provider key (`AspNetUserLogins`). All account-derived achievements
> (edit suggestions, pending contributions) depend on resolving this reliably.

### 5.2 Data model (proposed)

- **`Achievement`** — the catalog definition. Fields: `Id`, `Key` (stable slug),
  `Name`, `Description`, `Category`, `Icon`, `Points`, `Tier` (Bronze/Silver/Gold/… where
  applicable), `Threshold` (nullable, for count-based), `IsActivityOnly` (bool — cosmetic,
  does not affect level), `IsRetired` (bool), and ordering/grouping metadata.
- **`UserAchievement`** — an earned achievement. Fields: `UserId` (authenticated account),
  `ContributorName` (resolved GitHub username for display), `AchievementId`, `EarnedAtUtc`,
  and optional `Context` (e.g. the release/title that triggered a one-time award).
- **`UserAchievementProgress`** (optional but recommended) — cached progress toward
  un-earned count-based achievements: `UserId`, `AchievementId`, `Current`, `Target`,
  `UpdatedAtUtc`. Lets the profile and (Phase 2) browse page show progress cheaply.
- **Level** — derived, not stored as truth: computed from total achievement **points**
  against configured thresholds. May be cached on the user for display.
- **Audit** — an `AchievementAuditEntry` (or reuse an existing audit mechanism) recording
  award/revoke events with actor (system/backfill/admin), reason, and timestamp.

`TheDiscDbUser` currently has no profile/points/level fields; a cached `Level` /
`TotalPoints` may be added for display performance, but points/level remain **derivable**
from `UserAchievement` so they can always be recomputed.

### 5.3 Services & computation

- **`IAchievementService`** — evaluate and award achievements for a user; compute
  points/level; expose earned + progress for display.
- **Event-driven evaluation** — hook the points at which contribution/edit outcomes change
  to a rewardable state (contribution **Approved/Imported**, edit suggestion **approved**).
  Reuse the existing contribution/edit-suggestion lifecycle hooks rather than adding a new
  parallel pipeline.
- **Nightly reconciliation job** — recompute everyone's achievements/progress on a schedule
  to catch anything the event path missed (idempotent: awarding is "ensure earned", never
  double-awards).
- **Launch backfill** — a one-time run of the reconciliation logic across all existing
  contributors/history to award everything already earned.

### 5.4 Achievement catalog

All **count-based / level-affecting** achievements trigger on **post-review outcomes**
(contribution `Approved`/`Imported`, edit suggestion `approved`) so they inherit the
review workflow's quality gate. Items marked *cosmetic* are activity-only and do **not**
contribute points toward levels.

**🏅 Milestones (quality-gated)**
- **First Contribution** — first imported release.
- **First Suggested Edit** — first *approved* edit suggestion.
- **Contributor tiers** — 1 / 5 / 10 / 25 / 50 / 100 / 250 published releases
  (Bronze → Diamond).
- **Editor tiers** — 1 / 10 / 25 / 100 approved edit suggestions.

**🌐 Breadth / variety** (rewards filling gaps rather than repetition)
- **Format Collector** — releases across N distinct formats (DVD, Blu-ray, 4K UHD…).
- **Studio Explorer** — contributions spanning N distinct studios.
- **Decade Spanner** — releases spanning N distinct release decades.
- **Boxset Builder** — contributed N boxsets.

**✨ Quality / craft** (rewards getting it right)
- **First Try** — a contribution approved with zero change-requests.
- **Clean Streak** — N consecutive approved contributions with no rejections.
- **Complete Record** — a release submitted with all optional metadata filled
  (if measurable).

**🔥 Consistency / engagement**
- **Active Streak** — contributed in N consecutive months.
- **In the Works** — X contributions currently pending. *(cosmetic, activity-only)*
- **Comeback** — returned and contributed after a long inactivity gap. *(optional, fun)*

**🚩 Community / special (one-time)**
- **Pioneer** — among the first N contributors to the site.
- **First to Add** — first to add a given title to the database.

**📺 TV / Series** (effort-weighted — series contributions grant **more points** than
films, reflecting the greater effort; the title `Type` field distinguishes
`"Movie"` / `"Series"`)
- **Series Contributor** tiers — N published series releases.
- **Season Marathon** — contribute a full season (or N seasons) of one series.
- **Box Set Binger** — a multi-disc TV boxset.

**🎭 Genre-based** (`Genres` available as a comma-separated string per title)
- **Genre Specialist** — N releases in one genre, with flavored names
  (e.g. Horror Hoarder, Sci-Fi Specialist, Documentarian, Anime Aficionado).
- **Genre Hopper** — releases spanning N distinct genres (breadth).

### 5.5 Levels

Levels are **auto-derived from accumulated points** only (no manual assignment). Points
come from earned achievements (activity-only badges excluded). Proposed progression:

**Newcomer → Contributor → Archivist → Top Contributor → Curator**

(names and point thresholds to be finalized during implementation). Level is displayed on
the public profile and the leaderboard.

### 5.6 UI surfaces

- **`/contributedby/{username}`** (public profile) — the primary surface: contributor's
  level, earned badges (with icons/tooltips), and progress toward next achievements/level.
- **`/leaderboard`** — show each contributor's level and a compact badge indicator per row.
- **Phase 2 — Achievements browse & progress page** — a catalog view of all achievements
  grouped by category, showing earned vs locked state and progress bars toward un-earned
  count-based achievements.

## 6. Behavior & rules

- **Idempotent awarding** — evaluation is "ensure earned"; an achievement is awarded at
  most once. Nightly reconciliation and event-driven paths must never double-award.
- **Quality gating** — count/tier/level achievements only count contributions that reach
  `Approved`/`Imported` and edit suggestions that are `approved`. Submissions that are
  `Rejected`/`ChangesRequested` do not count.
- **Activity badges are cosmetic** — e.g. "In the Works" reflects current pending count and
  can go up and down; it awards no points and cannot raise a level. Spammed junk that never
  gets approved yields no lasting reward.
- **Effort weighting** — series contributions award more points than film contributions.
- **Backfill on launch** — existing history is evaluated once so current contributors
  immediately hold everything they've earned.
- **Level changes** — recomputed whenever points change; a level-up is a natural
  consequence of crossing a point threshold.
- **Retirement** — a retired achievement (`IsRetired`) stays awarded for those who earned
  it but is no longer grantable.

## 7. Risks & considerations

- **Gaming / data quality (primary concern)** — mitigated by gating meaningful achievements
  on **post-review outcomes**. The existing review workflow is the backstop: low-quality
  contributions get rejected and thus never earn points. Avoid any level-affecting
  achievement based on raw submission volume.
- **Identity reconciliation** — the account↔GitHub-username↔`Contributor.Name` link must be
  resolved reliably (see §5.1); a mismatch would mis-attribute or drop achievements.
- **Backfill cost / correctness** — the one-time backfill must be idempotent and safe to
  re-run; validate on a copy before running against production.
- **Performance** — computing progress on every page load could be expensive; cache
  progress (`UserAchievementProgress`) and level, refreshed on events + nightly.
- **Threshold tuning** — tiers/points should be tuned to feel meaningful given current
  contribution volumes; expect iteration.
- **Auditing** — award/revoke are user-facing state changes and must be audit-logged (actor,
  reason, timestamp) for transparency and debugging, consistent with the project's practice
  of auditing user-facing state-change features.

## 8. Open questions

- Exact account ↔ GitHub username mapping (§5.1) — `UserName` vs external-login provider key?
- Point values per achievement and per-tier, and the point thresholds that define each level.
- Do edit-suggestion achievements attribute to the same public profile as release
  contributions (requires the identity bridge to be solid)?
- Is "Complete Record" (all optional metadata) measurable from the current model?
- Badge icon/art source — reuse existing icon set or commission/select new art?
- Should any achievements be time-limited/seasonal (out of scope for now, but worth a flag)?

## 9. Rollout / phases

- **Phase 1** — data model + launch backfill + hybrid computation (event-driven + nightly)
  + full achievement catalog + auto-derived levels + display on `/contributedby/{username}`
  and `/leaderboard` + audit logging.
- **Phase 2** — dedicated achievements browse & progress page.

## 10. Future enhancements

- Achievement points-based leaderboard (or a toggle on the existing leaderboard).
- Seasonal / time-limited achievements and community challenges.
- Per-user visibility/privacy controls for badges.
- Shareable badge/level embeds or profile cards.
- Achievements for review/curation actions (once reviewer roles are broadened).
- Notifications when a badge is earned or a level is reached (tie into existing
  notification system).
