using TheDiscDb.InputModels;

namespace TheDiscDb.Search;

public static class SearchEntryExtensions
{
    public static IEnumerable<SearchEntry> ToSearchEntries(this MediaItem item)
    {
        yield return ToSearchEntry(item);

        if (item.Releases != null && item.Releases.Any())
        {
            foreach (var release in item.Releases)
            {
                foreach (var releaseEntry in ToSearchEntries(item, item.Releases))
                {
                    yield return releaseEntry;
                }

                if (release.Discs != null && release.Discs.Any())
                {
                    foreach (var discEntry in ToSearchEntries(item, release, release.Discs))
                    {
                        yield return discEntry;
                    }

                    foreach (var disc in release.Discs)
                    {
                        if (disc.Titles != null && disc.Titles.Any())
                        {
                            foreach (var titleEntry in ToSearchEntries(item, release, disc, disc.Titles))
                            {
                                yield return titleEntry;
                            }
                        }
                    }
                }
            }
        }
    }

    public static IEnumerable<SearchEntry> ToSearchEntries(this Boxset item)
    {
        var searchItem = new SearchEntry
        {
            id = string.Join('-', item.Id, "Boxset"),
            Type = "Boxset",
            Title = item.Title,
            ImageUrl = item.ImageUrl,
            RelativeUrl = $"/boxset/{item.Slug}",
            MediaItem = new ItemInfo
            {
                Slug = item.Slug,
                ImageUrl = item.ImageUrl
            }
        };

        yield return searchItem;

        if (item?.Release == null)
        {
            yield break;
        }

        foreach (var disc in item.Release.Discs)
        {
            searchItem = new SearchEntry
            {
                id = string.Join('-', disc.Id, "BoxsetDisc"),
                Type = "BoxsetDisc",
                Title = disc.Name,
                ImageUrl = item.ImageUrl,
                RelativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}",
                MediaItem = new ItemInfo
                {
                    Slug = item.Slug,
                    ImageUrl = item.ImageUrl
                },
                Release = new ItemInfo
                {
                    Slug = item.Release.Slug,
                    ImageUrl = item.Release.ImageUrl
                },
                Disc = new ItemInfo
                {
                    Slug = disc.Slug
                }
            };

            yield return searchItem;

            foreach (var title in disc.Titles)
            {
                if (title.Item != null)
                {
                    searchItem = new SearchEntry
                    {
                        id = string.Join('-', title.Id, "BoxsetTitle"),
                        Type = title.Item.Type,
                        Title = title.Item.Title,
                        ImageUrl = item.ImageUrl,
                        RelativeUrl = $"/boxset/{item.Slug}/discs/{disc.Index}",
                        MediaItem = new ItemInfo
                        {
                            Slug = item.Slug,
                            ImageUrl = item.ImageUrl
                        },
                        Release = new ItemInfo
                        {
                            Slug = item.Release.Slug,
                            ImageUrl = item.Release.ImageUrl
                        },
                        Disc = new ItemInfo
                        {
                            Slug = disc.Slug
                        }
                    };

                    yield return searchItem;
                }
            }
        }
    }

    private static SearchEntry ToSearchEntry(MediaItem item)
    {
        return new SearchEntry
        {
            id = string.Join('-', item.Type, item.Slug),
            Type = item.Type,
            Title = item.Title,
            ImageUrl = item.ImageUrl,
            RelativeUrl = $"/{item.Type}/{item.Slug}",
            MediaItem = new ItemInfo
            {
                Slug = item.Slug,
                ImageUrl = item.ImageUrl
            }
        };
    }

    private static IEnumerable<SearchEntry> ToSearchEntries(MediaItem item, IEnumerable<InputModels.Release> releases)
    {
        foreach (var release in releases)
        {
            var searchItem = new SearchEntry
            {
                id = string.Join('-', "Release", release.Slug),
                Type = "Release",
                Title = release.Title,
                ImageUrl = release.ImageUrl,
                RelativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}",
                MediaItem = new ItemInfo
                {
                    Slug = item.Slug,
                    ImageUrl = item.ImageUrl
                },
                Release = new ItemInfo
                {
                    Slug = release.Slug,
                    ImageUrl = release.ImageUrl
                }
            };

            yield return searchItem;
        }
    }

    private static IEnumerable<SearchEntry> ToSearchEntries(MediaItem item, InputModels.Release release, IEnumerable<InputModels.Disc> discs)
    {
        foreach (var disc in discs)
        {
            var searchItem = new SearchEntry
            {
                id = string.Join('-', disc.Id, "Disc"),
                Type = "Disc",
                Title = disc.Name,
                ImageUrl = release.ImageUrl,
                RelativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}/discs/{disc.Index}",
                MediaItem = new ItemInfo
                {
                    Slug = item.Slug,
                    ImageUrl = item.ImageUrl
                },
                Release = new ItemInfo
                {
                    Slug = release.Slug,
                    ImageUrl = release.ImageUrl
                },
                Disc = new ItemInfo
                {
                    Slug = disc.Slug
                }
            };

            yield return searchItem;
        }
    }

    private static IEnumerable<SearchEntry> ToSearchEntries(MediaItem item, InputModels.Release release, InputModels.Disc disc, IEnumerable<InputModels.Title> titles)
    {
        foreach (var title in disc.Titles)
        {
            if (title.Item != null)
            {
                var searchItem = new SearchEntry
                {
                    id = string.Join('-', title.Id, "Title"),
                    Type = title.Item.Type,
                    Title = title.Item.Title,
                    ImageUrl = release.ImageUrl,
                    RelativeUrl = $"/{item.Type}/{item.Slug}/releases/{release.Slug}/discs/{disc.Index}",
                    MediaItem = new ItemInfo
                    {
                        Slug = item.Slug,
                        ImageUrl = item.ImageUrl
                    },
                    Release = new ItemInfo
                    {
                        Slug = release.Slug,
                        ImageUrl = release.ImageUrl
                    },
                    Disc = new ItemInfo
                    {
                        Slug = disc.Slug
                    }
                };

                yield return searchItem;
            }
        }
    }
}
