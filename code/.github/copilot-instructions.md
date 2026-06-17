# Copilot Instructions

## Project Guidelines
- Never commit directly to the `main` branch. Always use a feature branch for changes.

## Build and Test

```shell
# Build (NuGetAudit=false required — private ADO feed is unreachable locally)
cd web/code
dotnet build TheDiscDb.slnx /p:NuGetAudit=false

# Tests — TUnit framework, run via dotnet run (not dotnet test)
cd web/code/TheDiscDb.UnitTests
dotnet run --no-build -c Debug

# Single test
dotnet run --no-build -c Debug -- --filter "FullyQualifiedName~MyTestName"
```

Run with Aspire: launch `web/code/TheDiscDb.AppHost` which orchestrates the web project, SQL Server, and Azurite blob storage.

## Code Style
- Razor files should be formatted with markup in the .razor file and code in a separate `.razor.cs` code-behind file. Do not use `@code` blocks in `.razor` files.
- Prefer record types over tuples for method return types. Define the record in the same file that uses it.
- Shared logic belongs in base classes (e.g., `ChangeBase<TDetails>`), not duplicated across siblings. Before adding a helper method to a concrete class, check if it belongs in the base.

## Domain: Data Table Rebuilds and Identity

Non-user data tables (Releases, Discs, Titles, Tracks, Chapters, MediaItems, BoxSets) can be **cleared and rewritten at any time** from the `data/` repo JSON files. This means:

- **Never store database `int` IDs** as foreign references in user-generated data (e.g., edit suggestions, contributions). Int IDs are unstable across rebuilds.
- **Use natural keys** (slug composites) to identify domain entities. For example, a Release is identified by `MediaItemSlug + ReleaseSlug` (or `BoxsetSlug + ReleaseSlug`), not by `Release.Id`.
- **Use `IdEncoder` (Sqids)** when exposing database IDs in user-facing URLs. Admin pages may use raw int IDs.

## URL Routing: LowercaseUrlMiddleware

`LowercaseUrlMiddleware` issues 301 redirects to lowercase any URL path that contains uppercase characters, **except** paths starting with prefixes listed in `CaseSensitivePrefixes`. When adding a new route prefix that contains case-sensitive tokens (like Sqids-encoded IDs), you **must** add it to `CaseSensitivePrefixes` in `LowercaseUrlMiddleware.cs`, or the IDs will be corrupted by lowercasing.

## Edit Suggestions: Snapshot-Diff Pattern

The edit suggestion system uses a snapshot-diff approach for partial updates:

- **`null` in a proposed change means "no change"**, not "set to null." The `SetIfChanged` helper in `ChangeBase` compares the proposed value against the original snapshot and only writes when they differ.
- Each `*Update` class implements `ApplyCoreAsync` which receives the deserialized original snapshot. Use `SetIfChanged(original, o => o.Field, proposed.Field, entity, (e, v) => e.Field = v)` for each field.
- The `AppendIfDifferent` helper (also in `ChangeBase`) handles string list fields the same way.