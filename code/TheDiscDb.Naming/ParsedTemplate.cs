using System;
using System.Collections.Generic;
using System.Text;

namespace TheDiscDb.Naming;

/// <summary>
/// Represents a successfully parsed naming template that can format output strings.
/// </summary>
public sealed class ParsedTemplate
{
    public IReadOnlyList<TemplateSegment> Segments { get; }

    internal ParsedTemplate(IReadOnlyList<TemplateSegment> segments)
    {
        Segments = segments;
    }

    /// <summary>
    /// Formats the template using the provided naming context.
    /// Missing values (null/empty/whitespace) trigger smart whitespace trimming
    /// of adjacent literal segments.
    /// </summary>
    public string Format(NamingContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Build an array of resolved string values per segment
        var resolved = new string?[Segments.Count];

        for (int i = 0; i < Segments.Count; i++)
        {
            var segment = Segments[i];
            if (segment is LiteralSegment lit)
            {
                resolved[i] = lit.Text;
            }
            else if (segment is TokenSegment tok)
            {
                var accessor = TokenDefinitions.GetAccessor(tok.TokenName);
                string? value = accessor(context);
                resolved[i] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        // Apply smart whitespace trimming for missing token values
        for (int i = 0; i < Segments.Count; i++)
        {
            if (Segments[i] is not TokenSegment || resolved[i] is not null)
            {
                continue;
            }

            // Token is missing — try to trim one adjacent space
            // Prefer trimming from the preceding literal
            if (i > 0 && resolved[i - 1] is string prev && prev.Length > 0 && prev[^1] == ' ')
            {
                resolved[i - 1] = prev[..^1];
            }
            else if (i + 1 < Segments.Count && resolved[i + 1] is string next && next.Length > 0 && next[0] == ' ')
            {
                resolved[i + 1] = next[1..];
            }
        }

        // Build output
        var sb = new StringBuilder();
        for (int i = 0; i < resolved.Length; i++)
        {
            if (resolved[i] is not null)
            {
                sb.Append(resolved[i]);
            }
        }

        return FileNameSanitizer.Sanitize(sb.ToString());
    }
}
