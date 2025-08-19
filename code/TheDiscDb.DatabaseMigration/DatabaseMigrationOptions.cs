namespace TheDiscDb.DatabaseMigration;

public class DatabaseMigrationOptions
{
    public string? DataDirectoryRoot { get; set; }
    public int MaxItemsToImportPerMediaType { get; set; } = 5;
}
