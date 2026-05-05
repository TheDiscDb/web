using System;
using System.Collections.Generic;

namespace TheDiscDb.Naming;

/// <summary>
/// Resolves the active naming template for a given disc-item type, falling
/// back to <see cref="DefaultFileNameTemplates"/> when the user has no
/// override. Caches parsed templates per item type for repeated formatting.
/// Invalid override strings are ignored (the default is used) so the resolver
/// is safe to call with raw, unvalidated user input — but callers should
/// validate templates with <see cref="NamingTemplate.Parse"/> on save.
/// </summary>
public sealed class FileNameTemplateResolver
{
    private readonly IReadOnlyDictionary<string, string> userOverrides;
    private readonly Dictionary<string, ParsedTemplate?> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public FileNameTemplateResolver(IReadOnlyDictionary<string, string>? userOverrides = null)
    {
        this.userOverrides = userOverrides ?? new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the parsed template that should be used when formatting file
    /// names for <paramref name="itemType"/>, or <c>null</c> when the type is
    /// unknown and no override exists.
    /// </summary>
    public ParsedTemplate? Resolve(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return null;
        }

        if (this.cache.TryGetValue(itemType, out var cached))
        {
            return cached;
        }

        ParsedTemplate? parsed = null;

        if (this.userOverrides.TryGetValue(itemType, out var overrideTemplate)
            && !string.IsNullOrWhiteSpace(overrideTemplate))
        {
            var result = NamingTemplate.Parse(overrideTemplate);
            if (result.IsSuccess)
            {
                parsed = result.Template;
            }
        }

        if (parsed is null)
        {
            var defaultTemplate = DefaultFileNameTemplates.GetDefault(itemType);
            if (defaultTemplate is not null)
            {
                var result = NamingTemplate.Parse(defaultTemplate);
                if (result.IsSuccess)
                {
                    parsed = result.Template;
                }
            }
        }

        this.cache[itemType] = parsed;
        return parsed;
    }

    /// <summary>
    /// Formats the file name for <paramref name="itemType"/>, returning an
    /// empty string when no template is available.
    /// </summary>
    public string Format(string? itemType, NamingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var template = this.Resolve(itemType);
        return template is null ? string.Empty : template.Format(context);
    }
}
