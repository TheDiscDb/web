namespace TheDiscDb.Client;

using System;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using TheDiscDb.Core.DiscHash;
using Fantastic.TheMovieDb;

public class TmdbClient
{
    private readonly TheMovieDbClient client;
    public TmdbClient(TheMovieDbClient client)
    {
        this.client = client;
    }

    public async Task<TmdbMovieSearchResponse> SearchMoviesAsync(TmdbSearchRequest request, CancellationToken cancellationToken = default)
    {
        var results = await this.client.SearchMovieAsync(request.Query, language: request.Language, year: request.Year ?? 0, cancellationToken: cancellationToken);
        return new TmdbMovieSearchResponse
        {
            Results = results.Results ?? new List<Fantastic.TheMovieDb.Models.SearchMovie>() 
        };
    }

    public async Task<TmdbSeriesSearchResponse> SearchSeriesAsync(TmdbSearchRequest request, CancellationToken cancellationToken = default)
    {
        var results = await this.client.SearchTvShowAsync(request.Query, language: request.Language, cancellationToken: cancellationToken);
        return new TmdbSeriesSearchResponse
        {
            Results = results.Results ?? new List<Fantastic.TheMovieDb.Models.SearchTv>()
        };
    }
}

public class NullFileSystemCache : Fantastic.TheMovieDb.Caching.FileSystem.IFileSystemCache
{
    public Task<T?> TryGet<T>(string cacheKey, Func<Task<T>> refresh, CancellationToken cancellationToken)
    {
        return Task<T?>.FromResult<T?>(default);
    }
}

public class TmdbSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public string? Language { get; set; }
    public int? Year { get; set; }
}

public class TmdbMovieSearchResponse
{
    public List<Fantastic.TheMovieDb.Models.SearchMovie> Results { get; set; } = new ();
}

public class TmdbSeriesSearchResponse
{
    public List<Fantastic.TheMovieDb.Models.SearchTv> Results { get; set; } = new();
}

public class HashClient
{
    private readonly HttpClient client;
    public HashClient(HttpClient client)
    {
        this.client = client;
    }
    public async Task<HashResponse> HashAsync(HashRequest request, CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsJsonAsync("/api/hash", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<HashResponse>(cancellationToken: cancellationToken);
        if (result == null)
        {
            throw new Exception("Unable to get hash result from server");
        }
        return result;
    }
}

public class HashRequest
{
    public List<FileHashInfo> Files { get; set; } = new List<FileHashInfo>();
}

public class HashResponse
{
    public string? Hash { get; set; }
}