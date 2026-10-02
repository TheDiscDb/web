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
}
