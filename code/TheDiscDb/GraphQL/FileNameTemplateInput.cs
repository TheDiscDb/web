namespace TheDiscDb.GraphQL;

/// <summary>
/// User-supplied template override for a single disc-item type.
/// Used by the <c>Title.filename(templates: [...])</c> resolver on the
/// public schema. Callers do not need to authenticate; missing item types
/// fall back to the built-in defaults.
/// </summary>
public sealed class FileNameTemplateInput
{
    public string ItemType { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
}
