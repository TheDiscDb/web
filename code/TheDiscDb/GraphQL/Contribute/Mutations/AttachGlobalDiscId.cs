using System.Text.RegularExpressions;
using HotChocolate.Authorization;
using Microsoft.AspNetCore.Identity;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Services.DiscId;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    // AACS Disc ID = 40 hex; DVD DVDDiscID = 32 hex.
    private static readonly Regex DiscIdPattern = new("^([0-9A-F]{32}|[0-9A-F]{40})$", RegexOptions.Compiled);

    /// <summary>
    /// Backfills a globally-stable Disc ID onto an existing disc, matched by its content-hash.
    /// The Disc ID itself is computed client-side — only file metadata (name/size) and the
    /// resulting hex string are sent here. Clean adds are written immediately and recorded as an
    /// approved (applied) change for the <c>/data</c> sync; a conflict with a different existing
    /// Disc ID leaves the database untouched and files a pending change for review.
    /// </summary>
    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<AttachDiscIdResult> AttachGlobalDiscId(
        List<FileHashInfo> files,
        string globalDiscId,
        UserManager<TheDiscDbUser> userManager,
        IDiscIdBackfillService backfillService,
        string? mediaItemSlug = null,
        string? boxsetSlug = null,
        string? releaseSlug = null,
        string? discSlug = null,
        int? discIndex = null,
        CancellationToken cancellationToken = default)
    {
        var user = principal.Principal ?? throw new AuthenticationException("No user principal available.");
        var userId = userManager.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("At least one file is required to identify the disc.", nameof(files));
        }

        var normalizedDiscId = (globalDiscId ?? string.Empty).Trim().ToUpperInvariant();
        if (!DiscIdPattern.IsMatch(normalizedDiscId))
        {
            throw new ArgumentException("Disc ID must be a 32- or 40-character hex string.", nameof(globalDiscId));
        }

        // Authoritative content-hash, computed the same way the database was built.
        var contentHash = files.OrderBy(f => f.Name).CalculateHash();

        var target = string.IsNullOrWhiteSpace(releaseSlug)
            ? null
            : new DiscTargetIdentity(mediaItemSlug, boxsetSlug, releaseSlug, discSlug, discIndex);

        return await backfillService.AttachAsync(userId, contentHash, normalizedDiscId, target, cancellationToken);
    }
}
