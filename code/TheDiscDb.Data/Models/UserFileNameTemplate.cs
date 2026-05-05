namespace TheDiscDb.Web.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using HotChocolate;

/// <summary>
/// Per-user override for the file-name template of a particular disc-item
/// type (e.g. "MainMovie", "Episode"). When a user has no row for an item
/// type, the built-in default in <c>TheDiscDb.Naming.DefaultFileNameTemplates</c>
/// is used.
/// </summary>
public class UserFileNameTemplate
{
    [JsonIgnore]
    public int Id { get; set; }

    [JsonIgnore]
    public string UserId { get; set; } = default!;

    /// <summary>
    /// The disc-item type this template applies to (e.g. "MainMovie",
    /// "Episode", "Extra", "Trailer", "DeletedScene").
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ItemType { get; set; } = default!;

    /// <summary>
    /// The user-supplied template string. Validated server-side with
    /// <c>NamingTemplate.Parse</c> before being persisted.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Template { get; set; } = default!;

    public DateTimeOffset UpdatedAt { get; set; }
}
