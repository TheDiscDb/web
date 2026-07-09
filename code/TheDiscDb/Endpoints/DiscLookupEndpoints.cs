using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.DiscLookup;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web;

/// <summary>
/// Anonymous, read-only lookup of a disc by its globally-stable Disc ID — for external tools
/// (MakeMKV/Jellyfin naming, re-extraction). Returns the disc's titles and their item mapping.
/// </summary>
public sealed class DiscLookupEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/disc-id");
        group.MapGet("{globalDiscId}", Lookup);
    }

    private static async Task<IResult> Lookup(
        string globalDiscId,
        IDbContextFactory<SqlServerDataContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        if (!DiscLookupQuery.IsValidDiscId(globalDiscId))
        {
            return TypedResults.BadRequest("Invalid disc id — must be a 32- or 40-character hex string.");
        }

        await using var database = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var result = await DiscLookupQuery.LookupAsync(database, globalDiscId, cancellationToken);

        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Json(result, JsonOptions);
    }
}
