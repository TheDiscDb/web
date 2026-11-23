namespace TheDiscDb.Client.Services;

using StrawberryShake;

public sealed record DiscSearchResult(
    string Hash,
    IReadOnlyList<DiscTitleMatch> Titles,
    IReadOnlyList<DiscReleaseMatch> Boxsets)
{
    public bool Found => this.Titles.Count > 0 || this.Boxsets.Count > 0;
}

public sealed class DiscSearchService(SearchDiscReleasesQuery query)
{
    public async Task<DiscSearchResult> SearchAsync(string hash, CancellationToken cancellationToken)
    {
        var titles = new List<DiscTitleMatch>();
        var boxsets = new List<DiscReleaseMatch>();
        var mediaCursors = new HashSet<string>(StringComparer.Ordinal);
        var boxsetCursors = new HashSet<string>(StringComparer.Ordinal);
        string? mediaAfter = null;
        string? boxsetAfter = null;
        bool includeMedia = true;
        bool includeBoxsets = true;
        hash = hash.ToUpperInvariant();

        while (includeMedia || includeBoxsets)
        {
            var response = await query.ExecuteAsync(
                hash: hash, mediaAfter: mediaAfter, boxsetAfter: boxsetAfter,
                includeMedia: includeMedia, includeBoxsets: includeBoxsets,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!response.IsSuccessResult() || response.Data is null)
            {
                throw new InvalidOperationException(
                    "Could not check the database for matching releases. Please retry.",
                    new InvalidOperationException(string.Join("; ", response.Errors.Select(error => error.Message))));
            }

            if (includeMedia)
            {
                var connection = response.Data.MediaItems;
                if (connection?.Nodes is null)
                {
                    throw LookupFailure();
                }

                foreach (var media in connection.Nodes)
                {
                    string type = media.Type?.ToLowerInvariant() ?? "";
                    var releases = media.Releases.Select(release => new DiscReleaseMatch(
                        release.Title ?? "Untitled release", release.Year,
                        $"/{type}/{Uri.EscapeDataString(media.Slug ?? "")}/releases/{Uri.EscapeDataString(release.Slug ?? "")}",
                        release.Upc, release.Discs.Select(disc => DiscLabel(disc.Name, disc.Index, disc.Format))
                            .Distinct(StringComparer.Ordinal).ToArray(), release.ImageUrl))
                        .DistinctBy(release => release.Url).ToArray();
                    if (releases.Length > 0)
                    {
                        titles.Add(new DiscTitleMatch(
                            media.Title ?? "Unknown title", media.Year, type, media.Externalids?.Tmdb,
                            media.Slug ?? "", releases));
                    }
                }

                includeMedia = connection.PageInfo.HasNextPage;
                mediaAfter = NextCursor(includeMedia, connection.PageInfo.EndCursor, mediaCursors);
            }

            if (includeBoxsets)
            {
                var connection = response.Data.Boxsets;
                if (connection?.Nodes is null)
                {
                    throw LookupFailure();
                }

                foreach (var boxset in connection.Nodes)
                {
                    if (boxset.Release is not { } release || release.Discs.Count == 0)
                    {
                        continue;
                    }

                    string title = boxset.Title ?? release.Title ?? "Untitled boxset";
                    if (!string.IsNullOrWhiteSpace(release.Title)
                        && !string.Equals(title, release.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        title = $"{title} - {release.Title}";
                    }
                    boxsets.Add(new DiscReleaseMatch(
                        title, release.Year, $"/boxset/{Uri.EscapeDataString(boxset.Slug ?? "")}",
                        release.Upc, release.Discs.Select(disc => DiscLabel(disc.Name, disc.Index, disc.Format))
                            .Distinct(StringComparer.Ordinal).ToArray(), boxset.ImageUrl));
                }

                includeBoxsets = connection.PageInfo.HasNextPage;
                boxsetAfter = NextCursor(includeBoxsets, connection.PageInfo.EndCursor, boxsetCursors);
            }
        }

        return new DiscSearchResult(hash,
            titles.GroupBy(title => (title.MediaType, title.Slug))
                .Select(group => group.First() with
                {
                    Releases = group.SelectMany(title => title.Releases).DistinctBy(release => release.Url).ToArray()
                })
                .OrderBy(title => title.Title, StringComparer.OrdinalIgnoreCase).ThenBy(title => title.Year).ToArray(),
            boxsets.DistinctBy(release => release.Url)
                .OrderBy(release => release.Title, StringComparer.OrdinalIgnoreCase).ThenBy(release => release.Year).ToArray());
    }

    private static string DiscLabel(string? name, int index, string? format)
        => $"{name ?? $"Disc {index}"} - {format}";

    private static string? NextCursor(bool hasNext, string? cursor, HashSet<string> seen)
    {
        if (hasNext && (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)))
        {
            throw LookupFailure();
        }
        return hasNext ? cursor : null;
    }

    private static InvalidOperationException LookupFailure()
        => new("Could not check the database for matching releases. Please retry.");
}
