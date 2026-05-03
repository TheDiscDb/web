using System.ComponentModel.DataAnnotations;

namespace TheDiscDb.Validation;

/// <summary>
/// Validates that a value is a 12 or 13 digit UPC/EAN. Centralised so every form
/// shares the same definition. Compose with <c>[Required]</c> when the field must
/// be filled in; on its own, this attribute treats null/empty as valid (standard
/// <see cref="RegularExpressionAttribute"/> behaviour).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class UpcAttribute : RegularExpressionAttribute
{
    public UpcAttribute() : base(@"^\d{12,13}$")
    {
        ErrorMessage = "UPC/EAN must be 12 or 13 digits";
    }
}
