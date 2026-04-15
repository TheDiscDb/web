namespace TheDiscDb.DatabaseMigration;

public class DatabaseMigrationOptions
{
    public string? DataDirectoryRoot { get; set; }
    public int MaxItemsToImportPerMediaType { get; set; } = 5;
    public int InitialSeedCount { get; set; } = 10;
    public bool SkipMigrationIfDataExists { get; set; } = true;
}
