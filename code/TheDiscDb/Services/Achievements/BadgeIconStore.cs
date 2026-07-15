namespace TheDiscDb.Services.Achievements;

using System.Collections.Concurrent;
using System.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;

/// <summary>
/// Loads and caches badge SVG markup from <c>wwwroot/badges/</c> so it can be inlined into
/// the DOM (allowing tier tinting via CSS <c>currentColor</c>). Missing icons return empty
/// markup rather than throwing.
/// </summary>
public sealed class BadgeIconStore(IWebHostEnvironment environment)
{
    private readonly ConcurrentDictionary<string, MarkupString> cache = new();

    public MarkupString GetSvg(string? iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
        {
            return default;
        }

        return cache.GetOrAdd(iconKey, Load);
    }

    private MarkupString Load(string iconKey)
    {
        // Guard against path traversal: only a bare file name is allowed.
        var safe = Path.GetFileNameWithoutExtension(iconKey);
        if (string.IsNullOrWhiteSpace(safe) || safe != iconKey)
        {
            return default;
        }

        var root = environment.WebRootPath;
        if (string.IsNullOrEmpty(root))
        {
            return default;
        }

        var path = Path.Combine(root, "badges", safe + ".svg");
        if (!File.Exists(path))
        {
            return default;
        }

        return new MarkupString(File.ReadAllText(path));
    }
}
