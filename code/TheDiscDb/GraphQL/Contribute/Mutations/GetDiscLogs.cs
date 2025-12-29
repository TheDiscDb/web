using FluentResults;
using MakeMkv;
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
    public async Task<DiscLogs> GetDiscLogs(string contributionId, string discId, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        // TODO: Check the user owns the contribution
        var blob = await this.assetStore.Download($"{contributionId}/{discId}-logs.txt", cancellationToken);
        if (blob == null)
        {
            throw new LogsNotFoundException(discId);
        }

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
            .FirstOrDefaultAsync(c => c.Id == decodedContributionId, cancellationToken);

        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        disc = contribution.Discs.FirstOrDefault(d => d.Id == decodedDiscId);
        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        return new DiscLogs
        {
            Info = organized,
            Disc = disc,
            Contribution = contribution
        };
    }
}
