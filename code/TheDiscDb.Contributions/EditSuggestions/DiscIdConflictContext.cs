namespace TheDiscDb.Services.EditSuggestions;

using System.Collections.Generic;

/// <summary>
/// A release-disc that shares the conflicted disc's content hash and is therefore a candidate
/// destination for the submitted Disc ID. Presented to an admin resolving a Disc ID conflict.
/// Only item (media-item) releases are candidates — boxset releases inherit their disc (and its
/// id) from the item release they were copied from, so they never independently own an AACS id.
/// </summary>
public sealed record DiscIdConflictCandidate(
    int ReleaseDiscId,
    string? MediaItemSlug,
    string ReleaseSlug,
    string? DiscSlug,
    int DiscIndex,
    string? ReleaseTitle,
    string? MediaTitle,
    string? CurrentGlobalDiscId);

/// <summary>
/// Everything an admin needs to resolve a conflicted <c>disc.fields.update</c> Disc ID change:
/// the submitted id, the id the target disc currently carries, and the other release-discs that
/// share the same content (candidate homes for the submitted id — typically a re-pressed sibling
/// edition that currently has no id).
/// </summary>
public sealed record DiscIdConflictContext(
    int SuggestionId,
    int ChangeId,
    string SubmittedGlobalDiscId,
    string? ContentHash,
    string TargetKey,
    string? TargetCurrentGlobalDiscId,
    IReadOnlyList<DiscIdConflictCandidate> Candidates);
