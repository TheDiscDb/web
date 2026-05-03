using System.ComponentModel.DataAnnotations;

namespace TheDiscDb.Validation;

/// <summary>
/// Validates that a value is a 10-character ASIN composed of ASCII alphanumerics.
/// Centralised so every form (boxset, contribution, edit, admin, engram) shares the
/// same definition. Compose with <c>[Required]</c> when the field must be filled in;
/// on its own, this attribute treats null/empty as valid (standard
/// <see cref="RegularExpressionAttribute"/> behaviour).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AsinAttribute : RegularExpressionAttribute
{
    public AsinAttribute() : base(@"^[A-Za-z0-9]{10}$")
    {
        ErrorMessage = "ASIN must be 10 alphanumeric characters";
    }
}
