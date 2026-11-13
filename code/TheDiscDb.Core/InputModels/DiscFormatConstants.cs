namespace TheDiscDb.InputModels;

public static class DiscFormatConstants
{
    public const string FourK = "4K";
    public const string Uhd = "UHD";
    public const string BluRay = "Blu-ray";
    public const string Dvd = "DVD";

    public static readonly string[] ContributionFormats = [FourK, BluRay, Dvd];

    public static string FromManifestFormat(string format) => format switch
    {
        "dvd" => Dvd,
        "blu-ray" => BluRay,
        "uhd-blu-ray" => FourK,
        _ => throw new ArgumentException($"Unsupported manifest disc format: {format}", nameof(format)),
    };
}
