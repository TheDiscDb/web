using System.Text.RegularExpressions;

namespace TheDiscDb.Client.Pages.Contribute;

public sealed record ContributionNamingSuggestion(string? SuggestedName, string? SuggestedSlug)
{
    public bool CanApply =>
        !string.IsNullOrWhiteSpace(this.SuggestedName) &&
        !string.IsNullOrWhiteSpace(this.SuggestedSlug);
}

public static partial class ContributionInputGuard
{
    public static ContributionNamingSuggestion? GetNamingSuggestion(
        string? mediaTitle,
        string? enteredName,
        string? enteredSlug,
        Func<string, string> createSlug)
    {
        if (string.IsNullOrWhiteSpace(mediaTitle))
        {
            return null;
        }

        bool nameContainsTitle = TryFindNormalizedPhrase(enteredName, mediaTitle, out var nameMatch);
        bool slugContainsTitle = TryFindNormalizedPhrase(enteredSlug, mediaTitle.Slugify(), out _);
        if (!nameContainsTitle && !slugContainsTitle)
        {
            return null;
        }

        string suggestedName = nameContainsTitle
            ? RemoveMatch(enteredName!, nameMatch)
            : enteredName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            return new ContributionNamingSuggestion(null, null);
        }

        return new ContributionNamingSuggestion(suggestedName, createSlug(suggestedName));
    }

    public static string? NormalizeNumber(string? value, bool allowRange)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string trimmed = value.Trim();
        if (!allowRange)
        {
            return NormalizeNumericSegment(trimmed);
        }

        string[] segments = trimmed.Split('-');
        if (segments.Length == 1)
        {
            return NormalizeNumericSegment(trimmed);
        }

        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment)))
        {
            return trimmed;
        }

        return string.Join('-', segments.Select(segment => NormalizeNumericSegment(segment.Trim())));
    }

    private static string NormalizeNumericSegment(string value)
    {
        if (!value.All(char.IsDigit))
        {
            return value;
        }

        string normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static bool TryFindNormalizedPhrase(string? value, string phrase, out TextRange match)
    {
        match = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var valueTokens = GetTokens(value);
        var phraseTokens = GetTokens(phrase);
        if (phraseTokens.Count == 0 || phraseTokens.Count > valueTokens.Count)
        {
            return false;
        }

        for (int start = 0; start <= valueTokens.Count - phraseTokens.Count; start++)
        {
            bool matches = true;
            for (int offset = 0; offset < phraseTokens.Count; offset++)
            {
                if (!string.Equals(
                    valueTokens[start + offset].Value,
                    phraseTokens[offset].Value,
                    StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                var first = valueTokens[start];
                var last = valueTokens[start + phraseTokens.Count - 1];
                match = new TextRange(first.Index, last.Index + last.Length);
                return true;
            }
        }

        return false;
    }

    private static List<TextToken> GetTokens(string value) =>
        [.. TokenRegex().Matches(value).Cast<Match>().Select(match =>
            new TextToken(match.Value, match.Index, match.Length))];

    private static string RemoveMatch(string value, TextRange match)
    {
        string left = TrimBoundary(value[..match.Start], trimEnd: true);
        string right = TrimBoundary(value[match.End..], trimEnd: false);

        return string.Join(' ', new[] { left, right }.Where(part => part.Length > 0));
    }

    private static string TrimBoundary(string value, bool trimEnd)
    {
        if (trimEnd)
        {
            int end = value.Length;
            while (end > 0 && IsBoundaryCharacter(value[end - 1]))
            {
                end--;
            }

            return value[..end].Trim();
        }

        int start = 0;
        while (start < value.Length && IsBoundaryCharacter(value[start]))
        {
            start++;
        }

        return value[start..].Trim();
    }

    private static bool IsBoundaryCharacter(char value) =>
        char.IsWhiteSpace(value) ||
        char.IsPunctuation(value) ||
        char.IsSymbol(value);

    [GeneratedRegex(@"[\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private readonly record struct TextToken(string Value, int Index, int Length);
    private readonly record struct TextRange(int Start, int End);
}
