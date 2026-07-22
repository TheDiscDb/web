namespace TheDiscDb.Services.Admin;

using TheDiscDb.InputModels;

/// <summary>
/// Shared helpers for contribution disc format values.
/// </summary>
internal static class ContributionDiscFormat
{
    /// <summary>
    /// Maps a contribution disc format string (e.g. "4K", "UHD", "Blu-ray", "DVD")
    /// to a friendly resolution string (e.g. "2160p").
    /// </summary>
    internal static string ResolveResolution(string? format)
    {
        if (format is null)
        {
            return "1080p";
        }

        if (format.Equals(DiscFormatConstants.FourK, StringComparison.OrdinalIgnoreCase) ||
            format.Equals(DiscFormatConstants.Uhd, StringComparison.OrdinalIgnoreCase))
        {
            return "2160p";
        }

        if (format.Equals(DiscFormatConstants.Dvd, StringComparison.OrdinalIgnoreCase))
        {
            return "720p";
        }

        return "1080p";
    }
}
