using FluentResults;
using HotChocolate.Authorization;
using MakeMkv;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
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
    public async Task<DiscLogs> GetDiscLogs(string contributionId, string discId, SqlServerDataContext database, UserManager<TheDiscDbUser> userManager, CancellationToken cancellationToken)
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

        string logPath = $"{contributionId}/{discId}-logs.txt";
        bool hasLogs = await this.assetStore.Exists(logPath, cancellationToken);
        if (!hasLogs)
        {
            return new DiscLogs
            {
                Info = null,
                Disc = disc,
                Contribution = contribution
            };
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
            throw new CouldNotParseLogsException(discId, ex);
        }

        return new DiscLogs
        {
            Info = organized,
            Disc = disc,
            Contribution = contribution
        };
    }
}
