namespace TheDiscDb.Naming;

/// <summary>
/// Canonical string values used for the disc-item type ("MainMovie", etc.).
/// Mirrors the values produced by <c>SummaryFileParser</c> and stored on
/// <c>DiscItemReference.Type</c>.
/// </summary>
public static class ItemTypeNames
{
    public const string MainMovie = "MainMovie";
    public const string Episode = "Episode";
    public const string Extra = "Extra";
    public const string Trailer = "Trailer";
    public const string DeletedScene = "DeletedScene";
}
