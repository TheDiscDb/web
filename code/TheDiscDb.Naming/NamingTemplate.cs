using System;
using System.Collections.Generic;
using System.Text;

namespace TheDiscDb.Naming;

public static class NamingTemplate
{
    /// <summary>
    /// Parses a template string into a <see cref="ParsedTemplate"/> or returns parse errors.
    /// Tokens are delimited by { and }. Use {{ and }} for literal braces.
    /// </summary>
    public static TemplateParseResult Parse(string template)
    {
        if (template is null)
        {
            return TemplateParseResult.Failure([new TemplateParseError("Template cannot be null.", 0, 0)]);
        }

        var segments = new List<TemplateSegment>();
        var errors = new List<TemplateParseError>();
        var literal = new StringBuilder();
        int i = 0;

        while (i < template.Length)
        {
            char c = template[i];

            if (c == '{')
            {
                // Check for escaped brace {{
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                // Flush any accumulated literal text
                if (literal.Length > 0)
                {
                    segments.Add(new LiteralSegment(literal.ToString()));
                    literal.Clear();
                }

                // Find the closing brace
                int tokenStart = i;
                int closingBrace = template.IndexOf('}', i + 1);

                if (closingBrace == -1)
                {
                    errors.Add(new TemplateParseError(
                        "Unclosed token. Expected '}' to close the token.",
                        tokenStart,
                        template.Length - tokenStart));
                    i = template.Length;
                    continue;
                }

                string tokenName = template.Substring(i + 1, closingBrace - i - 1);

                if (string.IsNullOrWhiteSpace(tokenName))
                {
                    errors.Add(new TemplateParseError(
                        "Empty token name.",
                        tokenStart,
                        closingBrace - tokenStart + 1));
                }
                else if (!TokenDefinitions.IsKnown(tokenName))
                {
                    errors.Add(new TemplateParseError(
                        $"Unknown token '{tokenName}'.",
                        tokenStart,
                        closingBrace - tokenStart + 1));
                }
                else
                {
                    segments.Add(new TokenSegment(tokenName, tokenStart));
                }

                i = closingBrace + 1;
            }
            else if (c == '}')
            {
                // Check for escaped brace }}
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }

                // Unmatched closing brace
                errors.Add(new TemplateParseError(
                    "Unexpected '}' without matching '{'.",
                    i,
                    1));
                i++;
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }

        // Flush remaining literal text
        if (literal.Length > 0)
        {
            segments.Add(new LiteralSegment(literal.ToString()));
        }

        if (errors.Count > 0)
        {
            return TemplateParseResult.Failure(errors);
        }

        return TemplateParseResult.Success(new ParsedTemplate(segments));
    }
}
