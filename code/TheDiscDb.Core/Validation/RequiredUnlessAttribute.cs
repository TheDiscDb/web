using System.ComponentModel.DataAnnotations;

namespace TheDiscDb.Validation;

/// <summary>
/// Requires a value unless a companion boolean property is true.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class RequiredUnlessAttribute : ValidationAttribute
{
    private readonly string conditionPropertyName;
    private readonly RequiredAttribute required = new();

    public RequiredUnlessAttribute(string conditionPropertyName)
    {
        this.conditionPropertyName = conditionPropertyName;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var conditionProperty = validationContext.ObjectType.GetProperty(conditionPropertyName)
            ?? throw new InvalidOperationException($"{conditionPropertyName} property was not found on {validationContext.ObjectType.Name}.");

        if (conditionProperty.GetValue(validationContext.ObjectInstance) is true)
        {
            return ValidationResult.Success;
        }

        return required.IsValid(value)
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), [validationContext.MemberName!]);
    }
}
