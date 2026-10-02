using FluentResults;
using HotChocolate.Authorization;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Contributions.OpticalDiscManifest;
using TheDiscDb.Services.Contributions;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(LogsNotFoundException))]
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(CouldNotParseLogsException))]
    [Error(typeof(AuthenticationException))]
    [Error(typeof(InvalidIdException))]
    [Error(typeof(InvalidOwnershipException))]
    [Authorize]
    public async Task<DiscLogs> GetDiscLogs(
        string contributionId,
        string discId,
        SqlServerDataContext database,
        UserManager<TheDiscDbUser> userManager,
        IOptions<DiscScanOptions> discScanOptions,
        CancellationToken cancellationToken)
    {
        var decodedContributionId = this.idEncoder.Decode(contributionId);
        var decodedDiscId = this.idEncoder.Decode(discId);
        UserContributionDisc? disc = null;
        UserContribution? contribution = null;

        contribution = await database.UserContributions
            .Include(c => c.Discs)
            .ThenInclude(c => c.Items)
                .ThenInclude(d => d.Chapters)
            .Include(c => c.Discs)
            .ThenInclude(c => c.Items)
                .ThenInclude(d => d.AudioTracks)
            .Include(c => c.Discs)
            .ThenInclude(c => c.Items)
                .ThenInclude(d => d.SubtitleTracks)
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        await EnsureOwnership(userManager, contribution, contributionId, discId, cancellationToken: cancellationToken);

        disc = contribution!.Discs.FirstOrDefault(d => d.Id == decodedDiscId);
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        string logPath = ContributionDiscAssets.LogsPath(contributionId, discId);
        string manifestPath = ContributionDiscAssets.ManifestPath(contributionId, discId);
        bool hasLogs = await this.assetStore.Exists(logPath, cancellationToken);
        bool hasManifest = await this.assetStore.Exists(manifestPath, cancellationToken);
        var preference = DiscLogSourceSelector.Select(disc, discScanOptions.Value.DiscScanMode, hasLogs, hasManifest);

        if (preference == DiscLogSourcePreference.ManifestFirst)
        {
            var manifestLogs = await GetDiscLogsFromManifest(manifestPath, disc, contribution, cancellationToken);
            if (manifestLogs.Info is not null || !hasLogs)
            {
                return manifestLogs;
            }
        }

        if (!hasLogs)
        {
            return await GetDiscLogsFromManifest(manifestPath, disc, contribution, cancellationToken);
        }

        var blob = await this.assetStore.Download(logPath, cancellationToken);

        DiscInfo? organized = null;

        try
        {
            string text = blob.ToString();
            var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var parsed = LogParser.Parse(lines);
            organized = LogParser.Organize(parsed);
        }
        catch (Exception ex)
        {
            throw new CouldNotParseLogsException(disc.Id.ToString(), ex);
        }

        return new DiscLogs
        {
            Info = organized,
            Disc = disc,
            Contribution = contribution
        };
    }

    private async Task<DiscLogs> GetDiscLogsFromManifest(string manifestPath, UserContributionDisc disc, UserContribution contribution, CancellationToken cancellationToken)
    {
        if (!await this.assetStore.Exists(manifestPath, cancellationToken))
        {
            return new DiscLogs
            {
                Info = null,
                Disc = disc,
                Contribution = contribution
            };
        }

        var blob = await this.assetStore.Download(manifestPath, cancellationToken);

        DiscInfo info;
        try
        {
            var parsed = new OpticalDiscManifestValidator().Parse(blob.ToString());
            if (!parsed.IsValid)
            {
                throw new InvalidOperationException(parsed.Error);
            }

            info = OpticalDiscManifestMapper.ToDiscInfo(parsed.Document!);
        }
        catch (Exception ex)
        {
            throw new CouldNotParseLogsException(disc.Id.ToString(), ex);
        }

        return new DiscLogs
        {
            Info = info,
            Disc = disc,
            Contribution = contribution
        };
    }
}
