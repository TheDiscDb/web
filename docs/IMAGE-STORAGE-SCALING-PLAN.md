# Image Storage Scaling Plan — move images out of the data git repo into Azure Blob

> Status: **Proposed / future work.** This is a reference design captured for later
> execution. No code in this document has been implemented yet.

## Problem

The `TheDiscDb/data` git repo holds both JSON metadata **and** the master image
files (`cover.jpg`, `front.jpg`, `back.jpg`). Images rarely change but bloat the
repo's history, making clones slow and storage costly. The goal is to make Azure
Blob Storage the master store for images, pull images out of the data repo, and
rewrite history so the repo shrinks.

## Decisions

1. **Git stays the source of truth for JSON metadata.** Only images move to blob.
2. **Image version history = simple naming convention.** When an image is replaced,
   the old blob is renamed/kept (e.g. `front.jpg` -> `front.<timestamp>.jpg`). No
   versioning tooling, no blob snapshots.
3. **Repo shrink = full history rewrite** of the `data` repo (`git filter-repo`/BFG),
   force-push. Breaking existing clones/forks is accepted.
4. **Local dev pulls images from the shared/production Azure blob container (read-only).**

## Current state (verified in code)

- **Runtime already serves images from blob**, not the repo: ImageSharp +
  `AzureBlobStorageImageProvider` reads from the container named by
  `BlobStorage:Container`, transcodes to WebP, caches in the `imagecache` container
  (`code/TheDiscDb/Program.cs:220-274`).
- **`ImageUrl` stored in DB/JSON is already the blob path** e.g. `movie/<slug>/cover.jpg`,
  `movie/<slug>/<release>.jpg`, `movie/<slug>/<release>-back.jpg`, `boxset/<slug>.jpg`
  (`code/TheDiscDb.Data.Import/Pipeline/CoverImageUploadMiddleware.cs`).
- **Two code paths upload repo images to blob today** (idempotent, skip-if-exists):
  - `BlobSyncService` (DatabaseMigration) — walks the repo, uploads any missing image.
  - `CoverImageUploadMiddleware` — during seed; also rewrites `ImageUrl` in the JSON.
- **The path that RE-INTRODUCES images into the repo**:
  `ContributionGeneratorService` writes `cover.jpg`/`front.jpg`/`back.jpg` into the
  data-repo workspace when generating files for an approved contribution
  (`code/TheDiscDb/Services/Admin/ContributionGeneratorService.cs:126,287-308`). This
  must be redirected to blob.
- Image blob layout is the canonical `IStaticAssetStore` path scheme; the master
  container = the existing serving container (`BlobStorage:Container`). No new container
  is strictly required, though a dedicated one is an option.

## Alternatives considered (and why not chosen)

- **Move metadata out of git too** (DB or doc store): rejected — git diff/PR review of
  metadata is valued. Keep git for JSON.
- **Blob versioning / snapshots** for image history: rejected as overkill — simple
  rename-on-replace satisfies the "keep old ones around" goal.
- **No history rewrite (stop-the-bleeding only)**: rejected — repo would stay large.
- **Fresh repo cutover**: rejected in favor of in-place history rewrite.

## Scope note: two repos

- **`web` repo (this repo):** code changes (redirect contribution image writes to blob,
  master-store rename-on-replace, local-dev read-only blob config, docs/guards).
- **`data` repo (separate):** the actual image extraction + history rewrite + force-push.
  This document covers the procedure; execution happens against that repo.

## Implementation phases

### Phase 1 — Make blob the authoritative master (web repo)
- Confirm/standardize the master container as `BlobStorage:Container`. Document the
  canonical path scheme in code/docs. (Optionally introduce a dedicated `images`
  master container distinct from any future use; default = reuse existing.)
- Add **rename-on-replace** to the image upload path: before overwriting an existing
  master blob, copy it to an archival name (`<name>.<yyyyMMddHHmmss>.jpg`). Implement
  in `BlobStorageStaticAssetStore` (or a small helper) and wire into the
  contribution-image upload path. Keep ImageSharp serving the live name.

### Phase 2 — Stop new images entering the data repo (web repo)
- Redirect `ContributionGeneratorService` image handling: upload approved
  front/back/cover images straight to the master images container (canonical path)
  instead of writing `.jpg` files into the data-repo workspace. Remove those `.jpg`
  paths from `generatedFiles` so they're not committed to the data repo.
- Verify the seed/import pipeline tolerates a repo with **no** local image files:
  `CoverImageUploadMiddleware`/`BlobSyncService` already `Exists`-check on disk and
  no-op when absent; `ImageUrl` comes from `metadata.json`/`release.json`. Add tests
  to lock this in.

### Phase 3 — One-time extraction + parity check (data repo + a tool/script)
- Run `BlobSyncService` (or a dedicated export pass) to guarantee **every** image in
  the repo exists in the master blob container. Produce a parity report
  (repo image count vs blob count; list any misses) and resolve gaps before deleting.

### Phase 4 — Remove images from repo HEAD + rewrite history (data repo)
- `git rm` all image files from the current tree; commit.
- Add `.gitignore` rules in the data repo to block `*.jpg`/`*.png` re-entry.
- Rewrite history with `git filter-repo` to purge image blobs from all commits;
  `git gc --prune=now --aggressive`; force-push. Communicate the breaking change and
  tag a pre-rewrite archive ref.
- Add a guard (pre-commit hook and/or CI check) rejecting image files in the data repo.

### Phase 5 — Local dev: read-only images from shared blob (web repo)
- Configure the ImageSharp Azure blob **image provider** to point at the shared/production
  images container (read-only connection string/SAS) for local dev, while keeping
  `contributions` and `imagecache` containers local (Azurite). Surface via AppHost/
  config (e.g. `BlobStorage:ImagesConnectionString`) with safe fallback.
- Document how a dev supplies the read-only connection string (user-secrets).

### Phase 6 — Docs & conventions
- Update `README.md`: data repo no longer contains images; how local dev gets images;
  the new contribution image flow.
- Add a `copilot-instructions.md` section documenting image-storage discipline
  (master = blob, repo is image-free, rename-on-replace, never commit images).

## Risks & considerations

- **History rewrite is destructive** (data repo): invalidates all hashes, breaks forks/
  open PRs/clones. Coordinate timing; announce; tag a pre-rewrite archive ref.
- **Parity before deletion is mandatory** — never `git rm`/rewrite until blob parity
  is proven, or images are lost.
- **Local dev needs network + read-only creds** to the shared blob; provide a fallback
  (placeholder images) so a dev without creds isn't fully blocked.
- **Rename-on-replace touches the image write path** — needs careful review/tests.
- **CDN/cache**: replaced images keep the same live blob name, so existing `imagecache`
  entries may need invalidation when an image is updated.

## Open follow-ups (not blocking)

- Whether to also serve images via a CDN in front of the blob container (future cost/perf).
- Whether to add a periodic reconciliation job (repo metadata <-> blob images).
