using System.Text.RegularExpressions;
using Fantastic.TheMovieDb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Components.Pages.Contribute;

public record SeasonInfo(string SeasonNumber, List<EpisodeInfo> Episodes);

public record EpisodeInfo(string Number, string Season, string Title, string FileName);

[Authorize]
public partial class EpisodeNames : ComponentBase
{
    [Parameter]
    public string? ExternalId { get; set; }

    [Inject]
    private TheMovieDbClient TmdbClient { get; set; } = null!;

    private List<SeasonInfo> seasons = new();

    protected override async Task OnInitializedAsync()
    {
        if (this.TmdbClient != null && !string.IsNullOrEmpty(this.ExternalId))
        {
            var series = await this.TmdbClient.GetSeries(this.ExternalId);
            SeasonInfo season0 = new SeasonInfo("0", new List<EpisodeInfo>());
            foreach (var season in series.Seasons)
            {
                var fullSeason = await this.TmdbClient.GetSeason(series.Id, season.SeasonNumber);
                SeasonInfo seasonInfo = new SeasonInfo(season.SeasonNumber.ToString(), new List<EpisodeInfo>());
                foreach (var episode in fullSeason.Episodes)
                {
                    string fileName = $"{series.Name}.S{season.SeasonNumber:00}.E{episode.EpisodeNumber:00}.{episode.Name}.mkv";
                    fileName = CleanPath(fileName);
                    var episodeInfo = new EpisodeInfo(episode.EpisodeNumber.ToString(), season.SeasonNumber.ToString(), episode.Name ?? string.Empty, fileName);

                    if (season.SeasonNumber == 0)
                    {
                        season0.Episodes.Add(episodeInfo);
                    }
                    else
                    {
                        seasonInfo.Episodes.Add(episodeInfo);
                    }
                }

                if (season.SeasonNumber != 0)
                {
                    this.seasons.Add(seasonInfo);
                }
            }

            if (season0.Episodes.Count > 0)
            {
                this.seasons.Add(season0);
            }
        }
    }

    public static string CleanPath(string name)
    {
        string invalidChars = Regex.Escape(new string(System.IO.Path.GetInvalidFileNameChars()));
        string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

        return Regex.Replace(name, invalidRegStr, "")
            .Replace('·', ' '); // makemkv doesn't like this char
    }
}