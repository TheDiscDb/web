namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fantastic.FileSystem;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.ImportModels;
using TheDiscDb.Web.Data;

public sealed class NewReleaseImportSelector
{
    private readonly IFileSystem fileSystem;
    private readonly IDbContextFactory<SqlServerDataContext> dbFactory;

    public NewReleaseImportSelector(IFileSystem fileSystem, IDbContextFactory<SqlServerDataContext> dbFactory)
    {
        this.fileSystem = fileSystem;
        this.dbFactory = dbFactory;
    }

    public async Task<ReleaseImportSelection> Select(string inputDirectory, CancellationToken cancellationToken = default)
    {
        string directory = Path.GetFullPath(inputDirectory);
        string releasePath = this.fileSystem.Path.Combine(directory, ReleaseFile.Filename);
        if (!await this.fileSystem.File.Exists(releasePath, cancellationToken))
        {
            return new(null, "input is not a single release directory");
        }

        string parentDirectory = this.fileSystem.Path.GetDirectoryName(directory);
        string metadataPath = this.fileSystem.Path.Combine(parentDirectory, MetadataFile.Filename);
        var metadata = JsonSerializer.Deserialize<MetadataFile>(
            await this.fileSystem.File.ReadAllText(metadataPath, cancellationToken), DataImporter.JsonOptions);
        var release = JsonSerializer.Deserialize<ReleaseFile>(
            await this.fileSystem.File.ReadAllText(releasePath, cancellationToken), DataImporter.JsonOptions);
        if (string.IsNullOrWhiteSpace(metadata?.Slug) || string.IsNullOrWhiteSpace(release?.Slug))
        {
            throw new InvalidDataException($"Parent metadata and release slugs are required to import '{directory}'.");
        }

        string type = metadata.Type?.ToLowerInvariant() switch
        {
            "movie" => "Movie",
            "series" => "Series",
            _ => throw new InvalidDataException($"Unsupported media type '{metadata.Type}' in '{metadataPath}'.")
        };

        await using var database = await this.dbFactory.CreateDbContextAsync(cancellationToken);
        var parent = await database.MediaItems
            .Where(item => item.Type == type && item.Slug == metadata.Slug)
            .Select(item => new
            {
                item.Id,
                HasReleases = item.Releases.Any(),
                HasTarget = item.Releases.Any(candidate => candidate.Slug == release.Slug)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (parent == null || !parent.HasReleases)
        {
            return new(null, "parent has no existing releases");
        }

        return parent.HasTarget
            ? new(null, "target release already exists")
            : new(new ReleaseImportScope(directory, release.Slug, parent.Id), "new release under an existing parent");
    }
}

public sealed record ReleaseImportSelection(ReleaseImportScope? Scope, string Reason);
