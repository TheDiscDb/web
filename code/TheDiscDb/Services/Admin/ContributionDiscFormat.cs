namespace TheDiscDb.Services.Admin;

/// <summary>
/// Shared helpers for contribution disc format values.
/// </summary>
internal static class ContributionDiscFormat
{
    /// <summary>
    /// Maps a contribution disc format string (e.g. "4K", "Blu-ray", "DVD")
    /// to a friendly resolution string (e.g. "2160p").
    /// </summary>
    internal static string ResolveResolution(string? format)
    {
        if (format is null)
        {
            return "1080p";
        }

        if (format.Equals("4K", StringComparison.OrdinalIgnoreCase))
        {
            return "2160p";
        }

        if (format.Equals("DVD", StringComparison.OrdinalIgnoreCase))
        {
            return "720p";
        }

        return "1080p";
    }
}
