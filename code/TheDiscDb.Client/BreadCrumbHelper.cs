using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb;

public static partial class BreadCrumbHelper
{
    public static (string Text, string Url) GetRootContributionLink() => (Text: "Contribute", Url: "/contribute");

    public static (string Text, string Url) GetContributionLink(IContributionDisplay contribution)
    {
        return (Text: GetContributionLinkText(contribution), Url: $"/contribution/{contribution.EncodedId}");
    }

    public static (string Text, string Url) GetContributionDiscLink(IContributionDisplay contribution, IContributiionDiscDisplay disc)
    {
        return (Text: $"{disc.Name} ({disc.Format})", Url: $"/contribution/{contribution.EncodedId}/discs/{disc.EncodedId}");
    }

    public static string GetContributionLinkText(IContributionDisplay contribution)
    {
        return $"{contribution.Title} ({contribution.Year}) - {contribution.ReleaseTitle}";
    }

    public static (string Text, string Url) GetAdminContributionLink(UserContribution? contribution) => (Text: $"{contribution?.Title}", Url: $"/admin/contribution/{contribution?.Id}");
    public static (string Text, string Url) GetAdminDiscLink(UserContribution? contribution, UserContributionDisc? disc) =>
        (Text: $"{disc?.Name}", Url: $"/admin/contribution/{contribution?.Id}/discs/{disc?.Id}");

    public static (string Text, string Url) GetRootAdminLink() => (Text: "Admin", Url: "/admin");
}

public static partial class BreadCrumbHelper
{
    public static (string Text, string Url) GetRootLink(IDisplayItem item)
    {
        if (item?.Type != null)
        {
            string type = item.Type.ToLower();

            if (type == "movie")
            {
                return (Text: "Movies", Url: "/movies");
            }
            else if (type == "series")
            {
                return (Text: "Series", Url: "/series");
            }
            else if (type == "boxset")
            {
                return GetBoxsetRootLink();
            }
        }

        return (string.Empty, string.Empty);
    }

    public static (string Text, string Url) GetBoxsetRootLink()
    {
        return (Text: "Boxsets", Url: "/boxsets");
    }

    public static (string Text, string Url) GetBoxsetLink(Boxset item)
    {
        if (item?.Title == null || item?.Slug == null)
        {
            return (string.Empty, string.Empty);
        }

        return (Text: item.Title, Url: $"/boxset/{item.Slug}");
    }

    public static (string Text, string Url) GetBoxsetDiscLink(Boxset item, Disc disc)
    {
        if (item?.Release?.Slug == null || item?.Slug == null || disc?.Index == null)
        {
            return (string.Empty, string.Empty);
        }

        return (
            Text: $"Disc {disc.Index}",
            Url: $"/boxset/{item.Slug}/releases/{item.Release.Slug}/discs/{disc.SlugOrIndex()}"
        );
    }

    public static (string Text, string Url) GetMediaItemLink(IDisplayItem item)
    {
        if (item?.Title == null || item?.Slug == null || item?.Type == null)
        {
            return (string.Empty, string.Empty);
        }

        return (
            Text: item.Title,
            Url: $"/{item.Type.ToLower()}/{item.Slug}"
        );
    }

    public static (string Text, string Url) GetReleaseLink(IDisplayItem item, IDisplayItem release)
    {
        if (release?.Title == null || item?.Slug == null || item?.Type == null)
        {
            return (string.Empty, string.Empty);
        }

        return (
            Text: release.Title,
            Url: $"/{item.Type.ToLower()}/{item.Slug}/releases/{release.Slug}"
        );
    }

    public static (string Text, string Url) GetDiscLink(IDisplayItem item, IDisplayItem release, IDisc disc)
    {
        if (item?.Type == null)
        {
            return (string.Empty, string.Empty);
        }

        string text = $"Disc {disc.Index}";
        if (!string.IsNullOrEmpty(disc.Name))
        {
            text = disc.Name;
        }

        return (
            Text: text,
            Url: $"/{item.Type.ToLower()}/{item.Slug}/releases/{release.Slug}/discs/{disc.SlugOrIndex()}"
        );
    }

    public static (string Text, string Url) GetDiscTitleLink(IDisplayItem item, Release release, Disc disc, string? titleSourceFile)
    {
        if (item?.Type == null)
        {
            return (string.Empty, string.Empty);
        }

        return (
            Text: $"Disc {disc.Index}",
            Url: $"/{item.Type.ToLower()}/{item.Slug}/releases/{release.Slug}/discs/{disc.SlugOrIndex()}/{NavigationExtensions.GetFile(titleSourceFile)}/{NavigationExtensions.GetExtension(titleSourceFile)}"
        );
    }

    public static string BuildCanonicalLink((string Text, string Url) link)
    {
        if (string.IsNullOrEmpty(link.Url))
        {
            return string.Empty;
        }
        return $"https://thediscdb.com{link.Url.ToLower()}";
    }

    public static (string Text, string Url) GetBoxsetDiscTitleLink(Boxset item, Disc disc, string titleSourceFile)
    {
        return (
            Text: $"Disc {disc.Index}",
            Url: $"/boxset/{item.Slug}/discs/{disc.Index}/{NavigationExtensions.GetFile(titleSourceFile)}/{NavigationExtensions.GetExtension(titleSourceFile)}"
        );
    }

    public static string GetFullName(this IDisc disc)
    {
        if (string.IsNullOrEmpty(disc.Name))
        {
            return $"Disc {disc.Index} ({disc.Format})";
        }

        return $"{disc.Name} ({disc.Format})";
    }
}