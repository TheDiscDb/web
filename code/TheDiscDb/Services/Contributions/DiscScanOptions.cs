namespace TheDiscDb.Services.Contributions;

public enum DiscScanMode
{
    Optional,
    Default
}

public sealed class DiscScanOptions
{
    public const string SectionName = "Contributions";

    public DiscScanMode DiscScanMode { get; set; } = DiscScanMode.Optional;

    // Offers a .odm.json file upload (from the thediscdb-scan CLI) on the optional scan page.
    public bool ShowScanFileUpload { get; set; }

    // Starts new contributions by scanning a disc instead of choosing a movie or series first.
    public bool DiscFirstFlow { get; set; }
}
