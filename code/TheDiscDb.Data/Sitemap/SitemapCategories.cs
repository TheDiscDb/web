namespace TheDiscDb.Web.Sitemap;

using System.Collections.Generic;

public static class SitemapCategories
{
    public const string Movies = "movies";
    public const string MoviesReleases = "movies-releases";
    public const string MoviesDiscs = "movies-discs";
    public const string MoviesTitles = "movies-titles";
    public const string Series = "series";
    public const string SeriesReleases = "series-releases";
    public const string SeriesDiscs = "series-discs";
    public const string SeriesTitles = "series-titles";
    public const string Boxsets = "boxsets";
    public const string Leaderboard = "leaderboard";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Movies, MoviesReleases, MoviesDiscs, MoviesTitles,
        Series, SeriesReleases, SeriesDiscs, SeriesTitles,
        Boxsets, Leaderboard
    };
}
