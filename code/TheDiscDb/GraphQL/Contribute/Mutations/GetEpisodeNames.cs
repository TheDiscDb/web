using System.Text;
using System.Text.Json;
using Fantastic.TheMovieDb;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Data.Import;
using TheDiscDb.GraphQL.Contribute.Exceptions;
using TheDiscDb.GraphQL.Contribute.Models;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Mutations;

public partial class ContributionMutations
{
    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(ExternalDataNotFoundException))]
    public async Task<SeriesEpisodeNames> GetEpisodeNames(string contributionId, SqlServerDataContext database, TheMovieDbClient tmdb, CancellationToken cancellationToken = default)
    {
        // First check blob storage to see if the episode names file exists
        string filePath = $"{contributionId}/episode-names.json";
        bool exists = await this.assetStore.Exists(filePath, cancellationToken);
        if (exists)
        {
            var data = await this.assetStore.Download(filePath, cancellationToken);
            string episodeJson = Encoding.UTF8.GetString(data.ToArray());
            return JsonSerializer.Deserialize<SeriesEpisodeNames>(episodeJson)!;
        }

        int id = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        var series = await tmdb.GetSeries(contribution.ExternalId, cancellationToken: cancellationToken);
        if (series == null)
        {
            throw new ExternalDataNotFoundException(contribution.ExternalId, "Series");
        }

        var year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;

        var results = new SeriesEpisodeNames
        {
            SeriesTitle = series.Name ?? "Unknown Title",
            SeriesYear = year.ToString()
        };

        foreach (var season in series.Seasons)
        {
            var fullSeason = await tmdb.GetSeason(series.Id, season.SeasonNumber);
            foreach (var episode in fullSeason.Episodes)
            {
                results.Episodes.Add(new SeriesEpisodeNameEntry
                {
                    SeasonNumber = season.SeasonNumber.ToString(),
                    EpisodeNumber = episode.EpisodeNumber.ToString(),
                    EpisodeName = episode.Name ?? "Unknown Title"
                });
            }
        }

        string json = JsonSerializer.Serialize(results, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Save to blob storage
        await this.assetStore.Save(new MemoryStream(Encoding.UTF8.GetBytes(json)), filePath, ContentTypes.JsonContentType, cancellationToken);
        return results!;
    }
}
