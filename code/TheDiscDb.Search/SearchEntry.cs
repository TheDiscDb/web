namespace TheDiscDb.Search;

public class SearchEntry
{
    public string? id { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? ImageUrl { get; set; }
    public string? RelativeUrl { get; set; }

    public ItemInfo? MediaItem { get; set; }
    public ItemInfo? Release { get; set; }
    public ItemInfo? Disc { get; set; }
}