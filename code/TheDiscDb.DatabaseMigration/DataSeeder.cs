using Fantastic.FileSystem;
using Microsoft.Extensions.Options;
using TheDiscDb.Data.Import;

namespace TheDiscDb.DatabaseMigration;

public class DataSeeder
{
    private readonly DataImporter dataImporter;
    private readonly IFileSystem fileSystem;
    private readonly IOptions<DatabaseMigrationOptions> options;

    public DataSeeder(DataImporter dataImporter, IFileSystem fileSystem, IOptions<DatabaseMigrationOptions> options)
    {
        this.dataImporter = dataImporter ?? throw new ArgumentNullException(nameof(dataImporter));
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SeedDataAsync(CancellationToken cancellationToken)
    {
        await SeedFromFolder("movie", cancellationToken);
        await SeedFromFolder("series", cancellationToken);
        await SeedFromFolder("sets", cancellationToken);
    }

    private async Task SeedFromFolder(string name, CancellationToken cancellationToken)
    {
        var items = await GetRandomSubdirectories(this.fileSystem.Path.Combine(options.Value.DataDirectoryRoot, name), options.Value.MaxItemsToImportPerMediaType, cancellationToken);
        foreach (var item in items)
        {
            await dataImporter.Import(item, cancellationToken);
        }
    }

    private async Task<IEnumerable<string>> GetRandomSubdirectories(string directory, int max, CancellationToken cancellationToken)
    {
        var subDirectories = await this.fileSystem.Directory.GetDirectories(directory, cancellationToken);
        var randomized = subDirectories.OrderBy(i => Guid.NewGuid()).Take(max);
        return randomized;
    }
}
