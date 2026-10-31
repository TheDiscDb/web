using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public partial class JsonDiff
{
    [Parameter]
    public string? OriginalJson { get; set; }

    [Parameter]
    public string? ProposedJson { get; set; }

    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public IReadOnlyCollection<string>? ExcludeKeys { get; set; }

    /// <summary>
    /// Fields whose existing values survive a missing, null, or empty proposal.
    /// This affects the preview only; nonempty proposals remain visible for review.
    /// </summary>
    [Parameter]
    public IReadOnlyCollection<string>? PreserveOnEmptyKeys { get; set; }

    private List<DiffLine> diffLines = [];

    protected override void OnParametersSet()
    {
        diffLines = ComputeDiff(OriginalJson, ProposedJson, ExcludeKeys, PreserveOnEmptyKeys);
    }

    private static List<DiffLine> ComputeDiff(
        string? originalJson,
        string? proposedJson,
        IReadOnlyCollection<string>? excludeKeys,
        IReadOnlyCollection<string>? preserveOnEmptyKeys)
    {
        var original = ParseFlat(originalJson);
        var proposed = ParseFlat(proposedJson);
        var lines = new List<DiffLine>();

        // An absent payload can mean entity deletion; only normalize fields within a proposal.
        if (!string.IsNullOrWhiteSpace(proposedJson) && preserveOnEmptyKeys is { Count: > 0 })
        {
            var preserved = new HashSet<string>(preserveOnEmptyKeys, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in original)
            {
                if (preserved.Contains(key)
                    && !string.IsNullOrEmpty(value)
                    && (!proposed.TryGetValue(key, out var proposedValue) || string.IsNullOrEmpty(proposedValue)))
                {
                    proposed[key] = value;
                }
            }
        }

        var allKeys = original.Keys.Union(proposed.Keys).ToList();

        if (excludeKeys is { Count: > 0 })
        {
            var excluded = new HashSet<string>(excludeKeys, StringComparer.OrdinalIgnoreCase);
            allKeys = allKeys.Where(k => !excluded.Contains(k)).ToList();
        }

        foreach (var key in allKeys)
        {
            var hasOriginal = original.TryGetValue(key, out var origValue);
            var hasProposed = proposed.TryGetValue(key, out var propValue);

            if (hasOriginal && hasProposed)
            {
                if (origValue == propValue)
                {
                    lines.Add(new DiffLine(" ", key, origValue ?? "null", "diff-unchanged"));
                }
                else
                {
                    lines.Add(new DiffLine("−", key, origValue ?? "null", "diff-removed"));
                    lines.Add(new DiffLine("+", key, propValue ?? "null", "diff-added"));
                }
            }
            else if (hasOriginal)
            {
                lines.Add(new DiffLine("−", key, origValue ?? "null", "diff-removed"));
            }
            else
            {
                lines.Add(new DiffLine("+", key, propValue ?? "null", "diff-added"));
            }
        }

        return lines;
    }

    private static Dictionary<string, string?> ParseFlat(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string?>();

        var result = new Dictionary<string, string?>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            FlattenElement(doc.RootElement, "", result);
        }
        catch (JsonException)
        {
            result["(raw)"] = json;
        }

        return result;
    }

    private static void FlattenElement(JsonElement element, string prefix, Dictionary<string, string?> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenElement(property.Value, key, result);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenElement(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;

            default:
                result[prefix] = element.ValueKind == JsonValueKind.Null ? null : element.ToString();
                break;
        }
    }

    private sealed record DiffLine(string Prefix, string Key, string Value, string CssClass);
}
