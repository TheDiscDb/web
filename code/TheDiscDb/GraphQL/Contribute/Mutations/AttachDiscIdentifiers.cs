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
    [GeneratedRegex("^([0-9A-F]{32}|[0-9A-F]{40})$")]
    private static partial Regex CombinedDiscIdPattern();

    [Error(typeof(AuthenticationException))]
    [Authorize]
    public async Task<AttachDiscIdentifiersResult> AttachDiscIdentifiers(
        List<FileHashInfo> files,
        List<DiscFingerprintFile> fingerprintFiles,
        UserManager<TheDiscDbUser> userManager,
        IDiscIdBackfillService backfillService,
        string? globalDiscId = null,
        string? mediaItemSlug = null,
        string? boxsetSlug = null,
        string? releaseSlug = null,
        string? discSlug = null,
        int? discIndex = null,
        CancellationToken cancellationToken = default)
    {
        var user = principal.Principal ?? throw new AuthenticationException(
            "No user principal available.");
        var userId = userManager.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            throw new AuthenticationException("UserId not found");
        }

        if (files is null || files.Count == 0)
        {
            throw new ArgumentException(
                "At least one legacy hash file is required to identify the disc.",
                nameof(files));
        }

        var normalizedDiscId = string.IsNullOrWhiteSpace(globalDiscId)
            ? null
            : globalDiscId.Trim().ToUpperInvariant();
        if (normalizedDiscId is not null && !CombinedDiscIdPattern().IsMatch(normalizedDiscId))
        {
            throw new ArgumentException(
                "Disc ID must be a 32- or 40-character hex string.",
                nameof(globalDiscId));
        }

        string contentHash = files.OrderBy(file => file.Name).CalculateHash();
        string fingerprint = DiscFingerprint.Calculate(fingerprintFiles);
        var target = string.IsNullOrWhiteSpace(releaseSlug)
            ? null
            : new DiscTargetIdentity(
                mediaItemSlug,
                boxsetSlug,
                releaseSlug,
                discSlug,
                discIndex);

        var matrixResult = await backfillService.AttachFingerprintAsync(
            userId,
            contentHash,
            fingerprint,
            target,
            cancellationToken);
        var globalDiscIdResult = normalizedDiscId is null
            ? null
            : await backfillService.AttachAsync(
                userId,
                contentHash,
                normalizedDiscId,
                target,
                cancellationToken);

        return new AttachDiscIdentifiersResult(globalDiscIdResult, matrixResult);
    }
}
