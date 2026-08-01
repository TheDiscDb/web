namespace TheDiscDb.GraphQL.Contribute;

using Microsoft.EntityFrameworkCore;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.Web.Data;

internal sealed record ExistingDiscPath(
    string MediaType,
    string ExternalId,
    string ReleaseSlug,
    string DiscSlug);

internal static class ExistingDiscPathValidator
{
    public static async Task<ExistingDiscPath> ValidateAsync(
        string existingDiscPath,
        SqlServerDataContext database,
        CancellationToken cancellationToken)
    {
        ExistingDiscPath path;

        try
        {
            var parsed = UserContributionDisc.ParseDiscPath(existingDiscPath);
            path = new ExistingDiscPath(
                parsed.MediaType,
                parsed.ExternalId,
                parsed.ReleaseSlug,
                parsed.DiscSlug);
        }
        catch (ArgumentException)
        {
            throw new InvalidDiscPathException(existingDiscPath);
        }

        var discKeyIsIndex = int.TryParse(path.DiscSlug, out var discIndex);
        var discExists = await database.ReleaseDiscs
            .AnyAsync(rd =>
                rd.Release != null &&
                rd.Release.Slug == path.ReleaseSlug &&
                rd.Release.MediaItem != null &&
                rd.Release.MediaItem.Type == path.MediaType &&
                rd.Release.MediaItem.Externalids.Tmdb == path.ExternalId &&
                (rd.Slug == path.DiscSlug ||
                    (discKeyIsIndex && (rd.Slug == null || rd.Slug == "") && rd.Index == discIndex)),
                cancellationToken);

        if (!discExists)
        {
            throw new InvalidDiscPathException(existingDiscPath);
        }

        return path;
    }
}
