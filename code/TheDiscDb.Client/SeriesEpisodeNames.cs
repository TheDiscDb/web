namespace TheDiscDb;

public class SeriesEpisodeNames
{
    public string SeriesTitle { get; set; } = string.Empty;
    public string SeriesYear { get; set; } = string.Empty;
    public ICollection<SeriesEpisodeNameEntry> Episodes { get; set; } = new List<SeriesEpisodeNameEntry>();

    public SeriesEpisodeNameEntry? TryFind(string season, string episode)
    {
        return Episodes.FirstOrDefault(e =>
            string.Equals(e.SeasonNumber, season, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.EpisodeNumber, episode, StringComparison.OrdinalIgnoreCase));
    }
}

public class SeriesEpisodeNameEntry
{
    public string SeasonNumber { get; set; } = string.Empty;
    public string EpisodeNumber { get; set; } = string.Empty;
    public string EpisodeName { get; set; } = string.Empty;
}