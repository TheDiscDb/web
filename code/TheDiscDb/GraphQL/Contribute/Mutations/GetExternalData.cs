using System.Text;
using System.Text.Json;
using Fantastic.TheMovieDb;
using Fantastic.TheMovieDb.Models;
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
    public async Task<ExternalMetadata> GetExternalData(string externalId, string mediaType, string provider, SqlServerDataContext database, TheMovieDbClient tmdb, CancellationToken cancellationToken = default)
    {
        int? year = null;
        ExternalMetadata? metadata = null;
        if (mediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var series = await tmdb.GetSeries(externalId, cancellationToken: cancellationToken);
            if (series == null)
            {
                throw new ExternalDataNotFoundException(externalId, "Series");
            }

            year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;

            metadata = new ExternalMetadata(series.Id, series.Name ?? "Unknown Title", year ?? 0, "https://image.tmdb.org/t/p/w500" + series?.PosterPath);
        }
        else
        {
            var movie = await tmdb.GetMovie(externalId, cancellationToken: cancellationToken);
            if (movie == null)
            {
                throw new ExternalDataNotFoundException(externalId, "Movie");
            }

            year = movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0;

            metadata = new ExternalMetadata(movie.Id, movie.Title ?? "Unknown Title", year ?? 0, "https://image.tmdb.org/t/p/w500" + movie?.PosterPath);
        }

        return metadata;
    }

    [Error(typeof(ContributionNotFoundException))]
    [Error(typeof(ExternalDataSerializationException))]
    [Error(typeof(ExternalDataNotFoundException))]
    public async Task<ExternalMetadata> GetExternalDataForContribution(string contributionId, SqlServerDataContext database, TheMovieDbClient tmdb, CancellationToken cancellationToken = default)
    {
        int id = this.idEncoder.Decode(contributionId);
        var contribution = await database.UserContributions
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contribution == null)
        {
            throw new ContributionNotFoundException(contributionId);
        }

        // First check blob storage to see if the episode names file exists
        string filePath = $"{contributionId}/tmdb.json";
        bool exists = await this.assetStore.Exists(filePath, cancellationToken);
        if (exists)
        {
            var data = await this.assetStore.Download(filePath, cancellationToken);
            string existingJson = Encoding.UTF8.GetString(data.ToArray());

            if (contribution.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
            {
                var series = JsonSerializer.Deserialize<Series>(existingJson);
                if (series == null)
                {
                    throw new ExternalDataSerializationException("Failed to deserialize series data from blob storage");
                }

                return new ExternalMetadata(series.Id, series.Name ?? "Unknown Title", series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0, "https://image.tmdb.org/t/p/w500" + series?.PosterPath);
            }
            else
            {
                var movie = JsonSerializer.Deserialize<Movie>(existingJson);
                if (movie == null)
                {
                    throw new ExternalDataSerializationException("Failed to deserialize movie data from blob storage");
                }

                return new ExternalMetadata(movie.Id, movie.Title ?? "Unknown Title", movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0, "https://image.tmdb.org/t/p/w500" + movie?.PosterPath);
            }
        }

        string? json = null;
        int? year = null;
        ExternalMetadata? metadata = null;

        if (contribution.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
        {
            var series = await tmdb.GetSeries(contribution.ExternalId, cancellationToken: cancellationToken);
            if (series == null)
            {
                throw new ExternalDataNotFoundException(contribution.ExternalId, "Series");
            }

            year = series.FirstAirDate.HasValue ? series.FirstAirDate.Value.Year : 0;
            json = JsonSerializer.Serialize(series, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            metadata = new ExternalMetadata(series.Id, series.Name ?? "Unknown Title", year ?? 0, "https://image.tmdb.org/t/p/w500" + series?.PosterPath);
        }
        else
        {
            var movie = await tmdb.GetMovie(contribution.ExternalId, cancellationToken: cancellationToken);
            if (movie == null)
            {
                throw new ExternalDataNotFoundException(contribution.ExternalId, "Movie");
            }

            year = movie.ReleaseDate.HasValue ? movie.ReleaseDate.Value.Year : 0;
            json = JsonSerializer.Serialize(movie, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            metadata = new ExternalMetadata(movie.Id, movie.Title ?? "Unknown Title", year ?? 0, "https://image.tmdb.org/t/p/w500" + movie?.PosterPath);
        }


        // Save to blob storage
        if (!string.IsNullOrEmpty(json))
        {
            await this.assetStore.Save(new MemoryStream(Encoding.UTF8.GetBytes(json)), filePath, ContentTypes.JsonContentType, cancellationToken);
        }

        return metadata;
    }
}
