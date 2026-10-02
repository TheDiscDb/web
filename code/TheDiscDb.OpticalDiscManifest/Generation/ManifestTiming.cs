namespace TheDiscDb.OpticalDiscManifest.Generation;

internal static class ManifestTiming
{
    internal static double TicksToSeconds(long ticks)
        => RoundSeconds(ticks / 45_000d);

    internal static double RoundSeconds(double seconds)
        => Math.Round(seconds, 6, MidpointRounding.AwayFromZero);
}
