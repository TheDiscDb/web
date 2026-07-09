namespace TheDiscDb.Services.Contributions;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MakeMkv;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb;
using TheDiscDb.Data.Import;
using TheDiscDb.Web.Data;

/// <summary>
/// Creates a pending user contribution for a single disc (used when a contributor inserts a disc
/// that isn't yet in the database) and stores its MakeMKV logs, using the same content conventions
/// as the web contribute flow (<c>CreateContribution</c> / <c>CreateDisc</c> / disc-logs upload).
/// The caller must supply the <b>contributions</b> asset store.
/// </summary>
public sealed class ContributionDiscService(
    SqlServerDataContext database,
    IStaticAssetStore assetStore,
    SqidsEncoder<int> idEncoder) : IContributionDiscService
{
    public async Task<ContributionDiscResult> CreatePendingDiscContributionAsync(
        string userId,
        ContributionDiscRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.ContentHash);

        var disc = new UserContributionDisc
        {
            ContentHash = request.ContentHash,
            GlobalDiscId = request.GlobalDiscId,
            Format = request.Format,
            Name = request.Name,
            Slug = request.Slug,
            Index = 1,
            ExistingDiscPath = string.Empty,
        };

        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Status = UserContributionStatus.Pending,
            MediaType = request.MediaType,
            ExternalId = request.ExternalId,
            ExternalProvider = request.ExternalProvider,
            Asin = request.Asin,
            Upc = request.Upc,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug,
            Locale = request.Locale,
            RegionCode = request.RegionCode,
            Title = request.Title ?? string.Empty,
            Year = request.Year ?? string.Empty,
            TitleSlug = CreateSlug(request.Title, request.Year),
            FrontImageUrl = string.Empty,
            BackImageUrl = string.Empty,
        };

        contribution.Discs.Add(disc);
        database.UserContributions.Add(contribution);
        await database.SaveChangesAsync(cancellationToken);

        var encodedContributionId = idEncoder.Encode(contribution.Id);
        var encodedDiscId = idEncoder.Encode(disc.Id);

        if (!string.IsNullOrWhiteSpace(request.DiscLogs))
        {
            var (ok, error) = await TrySaveLogsAsync(encodedContributionId, encodedDiscId, request.DiscLogs!, cancellationToken);
            disc.LogsUploaded = ok;
            disc.LogUploadError = error;
            await database.SaveChangesAsync(cancellationToken);
        }

        return new ContributionDiscResult(
            contribution.Id, disc.Id, encodedContributionId, encodedDiscId, disc.LogsUploaded, disc.LogUploadError);
    }

    public async Task<ContributionDiscResult?> AddDiscToContributionAsync(
        string userId,
        string encodedContributionId,
        ContributionDiscRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        ArgumentException.ThrowIfNullOrEmpty(encodedContributionId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.ContentHash);

        int contributionId;
        try
        {
            var decoded = idEncoder.Decode(encodedContributionId);
            if (decoded.Count != 1)
            {
                return null;
            }

            contributionId = decoded[0];
        }
        catch (Exception)
        {
            return null;
        }

        var contribution = await database.UserContributions
            .Include(c => c.Discs)
            .FirstOrDefaultAsync(c => c.Id == contributionId, cancellationToken);

        // Not found, or not owned by this user.
        if (contribution is null || !string.Equals(contribution.UserId, userId, StringComparison.Ordinal))
        {
            return null;
        }

        // Idempotent on ContentHash: reuse a disc that's already on the contribution.
        var disc = contribution.Discs.FirstOrDefault(d => d.ContentHash == request.ContentHash);
        if (disc is null)
        {
            var maxIndex = contribution.Discs.Count > 0 ? contribution.Discs.Max(d => d.Index ?? 0) : 0;
            disc = new UserContributionDisc
            {
                ContentHash = request.ContentHash,
                GlobalDiscId = request.GlobalDiscId,
                Format = request.Format,
                Name = request.Name,
                Slug = request.Slug,
                Index = maxIndex + 1,
                ExistingDiscPath = string.Empty,
            };
            contribution.Discs.Add(disc);
        }
        else
        {
            // Add-only: fill in a missing Disc ID without clobbering an existing one.
            disc.GlobalDiscId ??= request.GlobalDiscId;
        }

        await database.SaveChangesAsync(cancellationToken);

        var encodedContribution = idEncoder.Encode(contribution.Id);
        var encodedDiscId = idEncoder.Encode(disc.Id);

        if (!string.IsNullOrWhiteSpace(request.DiscLogs))
        {
            var (ok, error) = await TrySaveLogsAsync(encodedContribution, encodedDiscId, request.DiscLogs!, cancellationToken);
            disc.LogsUploaded = ok;
            disc.LogUploadError = error;
            await database.SaveChangesAsync(cancellationToken);
        }

        return new ContributionDiscResult(
            contribution.Id, disc.Id, encodedContribution, encodedDiscId, disc.LogsUploaded, disc.LogUploadError);
    }

    // Mirrors the web SaveDiscLogsInternal: normalize to CRLF, validate as MakeMKV, then store at
    // the canonical contributions path. Returns (uploaded, error) rather than throwing.
    private async Task<(bool Uploaded, string? Error)> TrySaveLogsAsync(
        string encodedContributionId, string encodedDiscId, string logs, CancellationToken cancellationToken)
    {
        // Normalize any CRLF to LF first, then convert LF to CRLF.
        logs = logs.Replace("\r\n", "\n").Replace("\n", "\r\n");

        var lines = logs.Split("\r\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            var parsed = LogParser.Parse(lines).ToList();
            if (parsed.Count == 0)
            {
                return (false, "Log file contains no valid MakeMKV log entries");
            }

            LogParser.Organize(parsed);
        }
        catch (Exception)
        {
            return (false, "Could not parse log file");
        }

        var byteArray = Encoding.UTF8.GetBytes(logs);
        using var memoryStream = new MemoryStream(byteArray);
        await assetStore.Save(
            memoryStream,
            $"{encodedContributionId}/{encodedDiscId}-logs.txt",
            ContentTypes.TextContentType,
            cancellationToken);

        return (true, null);
    }

    private static string CreateSlug(string? name, string? year)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(year) ? name.Slugify() : $"{name.Slugify()}-{year}";
    }
}
