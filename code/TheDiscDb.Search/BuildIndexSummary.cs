namespace TheDiscDb.Search;

public class BuildIndexSummary
{
    public bool Success { get; set; }
    public int ItemCount { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}