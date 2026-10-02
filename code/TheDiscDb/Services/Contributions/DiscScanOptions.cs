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
}
