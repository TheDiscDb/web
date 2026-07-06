# Project Plans Index

This folder holds design docs and plans for larger, cross-cutting bodies of work
on TheDiscDb. Use this index to track what is planned, in progress, or captured
for the future across the whole project.

> **Status legend**
> - 🟢 **Shipped** — implemented and merged.
> - 🟡 **In progress / backlog** — partially delivered, or an active backlog of candidate work.
> - 🔵 **Proposed** — captured design/reference; no implementation started.

## Plans

| Plan | Status | Summary |
|------|--------|---------|
| [Image Storage Scaling](IMAGE-STORAGE-SCALING-PLAN.md) | 🔵 Proposed | Make Azure Blob the master store for images, pull image files out of the `data` git repo, and rewrite history to shrink the repo. JSON metadata stays in git. Reference design only — no code implemented. |
| [Canonical Disc Deduplication (Phase 4)](PHASE-4-CANONICAL-DISC-DEDUPLICATION.md) | 🔵 Proposed | Refactor the disc storage model so identical disc content (tracks + chapters) shared across releases is stored once and referenced, rather than duplicated per release. |
| [Edit Suggestions (Phase 2)](EDIT-SUGGESTIONS-PHASE-2.md) | 🟡 In progress / backlog | Phase 1 (suggest/review edits to existing records) shipped. Phase 2 is a backlog broadening what can be suggested, filling metadata gaps, and maturing the admin review-to-publish workflow. |

## Conventions

- One doc per major initiative. Prefer an ALL-CAPS, hyphenated file name that
  describes the initiative (e.g. `IMAGE-STORAGE-SCALING-PLAN.md`).
- Start each plan with an `## Overview` and, where useful, a status note at the top.
- When you add or retire a plan, update the table above so this stays the single
  place to see the overall roadmap.
- When a plan is fully delivered, mark it 🟢 **Shipped** here (optionally move
  detail into code/docs) rather than silently deleting the doc.
