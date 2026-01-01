using FluentResults;
using HotChocolate.Authorization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(DiscNotFoundException))]
    [Error(typeof(FieldRequiredException))]
    [Error(typeof(InvalidIdException))]
    [Authorize]
    public async Task<DiscUploadStatus> GetDiscUploadStatus(string discId, SqlServerDataContext database, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(discId))
        {
            throw new FieldRequiredException("discId");
        }

        int realDiscId = this.idEncoder.Decode(discId);

        if (realDiscId == 0)
        {
            throw new InvalidIdException(discId, "Disc");
        }

        var disc = await database.UserContributionDiscs
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == realDiscId, cancellationToken);

        if (disc == null)
        {
            throw new DiscNotFoundException(discId);
        }

        return new DiscUploadStatus(disc.LogsUploaded);
    }
}
