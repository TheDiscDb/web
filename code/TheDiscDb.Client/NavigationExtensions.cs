using TheDiscDb.InputModels;

namespace TheDiscDb;

public static class NavigationExtensions
{
    public static string ItemDetailUrl(this IDisplayItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        return $"/{item.ItemTypeForUrl()}/{item.Slug}";
    }

    public static string ItemTypeForUrl(this IDisplayItem item)
    {
        if (item?.Type == null)
        {
            return string.Empty;
        }

        return item.Type.ToLower();
    }

    public static string ReleaseListUrl(this IDisplayItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        return $"{item.ItemDetailUrl()}/releases";
    }

    public static string ReleaseDetailUrl(this Release release, IDisplayItem parentItem)
    {
        if (release == null)
        {
            return string.Empty;
        }

        return $"{parentItem.ReleaseListUrl()}/{release.Slug}";
    }

    public static string DiscDetailUrl(this Disc disc, Release release, IDisplayItem parentItem)
    {
        if (disc == null)
        {
            return string.Empty;
        }

        return $"{release.ReleaseDetailUrl(parentItem)}/discs/{disc.SlugOrIndex()}";
    }

    ///Movie/the-marvels-2023/releases/2024-cinematic-universe-edition-4k/discs/1/00801/mpls
    public static string TitleDetailUrl(this Title title, Disc disc, Release release, IDisplayItem parentItem)
    {
        if (title == null)
        {
            return string.Empty;
        }

        return $"{disc.DiscDetailUrl(release, parentItem)}/{title.UrlComponent()}";
    }

    public static string UrlComponent(this Title title)
    {
        if (title?.SourceFile == null)
        {
            return string.Empty;
        }

        return $"{GetFile(title.SourceFile)}/{GetExtension(title.SourceFile)}";
    }

    public static string FullDiscName(this Disc disc)
    {
        if (string.IsNullOrEmpty(disc.Name))
        {
            return $"Disc {disc.Index} ({disc.Format})";
        }

        return disc.Name;
    }

    public static string TitleDescription(this Title title)
    {
        string? result = title?.Item?.Title;

        if (result != null)
        {
            return result;
        }

        return string.Empty;
    }

    public static string ItemType(this Title title)
    {
        string? result = title?.Item?.Type;

        if (result != null)
        {
            return result;
        }

        return string.Empty;
    }

    public static string EncodeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return extension.Replace("(", "[").Replace(")", "]");
    }

    public static string DecodeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return extension.Replace("[", "(").Replace("]", ")");
    }

    public static string GetFile(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return string.Empty;
        }

        var parts = file.Split('.');
        if (parts.Length == 2)
        {
            return parts[0];
        }

        return file;
    }

    public static string GetExtension(string? file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return string.Empty;
        }

        var parts = file.Split('.');
        if (parts.Length == 2)
        {
            return EncodeExtension(parts[1]);
        }

        return string.Empty;
    }

    public static string GetSourceFile(string? file, string? extension)
    {
        if (!string.IsNullOrEmpty(file))
        {
            if (!string.IsNullOrEmpty(extension) && extension != "-")
            {
                return $"{file}.{DecodeExtension(extension)}";
            }

            return file;
        }

        return string.Empty;
    }

    public static string GetDesciption(this Track track)
    {
        if (track?.Type != null)
        {
            string type = track.Type.ToLower();
            if (type == "video")
            {
                return $"{track.Name} {track.AspectRatio} ({track.Resolution})";
            }
            else if (type == "audio")
            {
                return $"{track.Name} {track.AudioType} ({track.Language})";
            }
            else if (type == "subtitles")
            {
                return $"{track.Name} ({track.Language})";
            }
        }

        return string.Empty;
    }

    public static IEnumerable<DiscFeature> GetDiscFeatures(InputModels.Disc disc)
    {
        var features = new Dictionary<string, DiscFeature>();

        foreach (var title in disc.Titles.Where(t => t.Item != null))
        {
            if (title?.Item?.Type == null)
            {
                continue;
            }

            if (features.TryGetValue(title.Item.Type, out DiscFeature? f))
            {
                f.Count++;
            }
            else
            {
                bool hasChapters = title.Item.Chapters.Any();
                features[title.Item.Type] = new DiscFeature
                {
                    HasChapters = hasChapters,
                    Count = 1,
                    Type = title.Item.Type
                };
            }
        }

        return features.Select(i => i.Value);
    }
}

public class DiscFeature
{
    public bool HasChapters { get; set; }
    public int Count { get; set; }
    public string? Type { get; set; }
    public string Description
    {
        get
        {
            if (string.IsNullOrEmpty(Type))
            {
                return string.Empty;
            }

            if (Type == "MainMovie")
            {
                if (Count == 1)
                {
                    return "1 movie";
                }
                else
                {
                    return Count + " movies";
                }
            }
            else if (Type == "Extra")
            {
                if (Count == 1)
                {
                    return "1 extra";
                }
                else
                {
                    return Count + " extras";
                }
            }
            else if (Type == "Trailer")
            {
                if (Count == 1)
                {
                    return "1 trailer";
                }
                else
                {
                    return Count + " trailers";
                }
            }
            else if (Type == "DeletedScene")
            {
                if (Count == 1)
                {
                    return "1 deleted scene";
                }
                else
                {
                    return Count + " deleted scenes";
                }
            }
            else if (Type == "Episode")
            {
                if (Count == 1)
                {
                    return "1 episode";
                }
                else
                {
                    return Count + " episodes";
                }
            }

            return "";
        }
    }
}