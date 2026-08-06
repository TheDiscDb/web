namespace TheDiscDb.Web.Data;

using System;
using System.Globalization;
using System.Linq;
using TheDiscDb.InputModels;

public static class IntakeMatchNormalizer
{
    public static string ExternalProvider(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    public static string ExternalId(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId)
            ? numericId.ToString(CultureInfo.InvariantCulture)
            : normalized.ToUpperInvariant();
    }

    public static string Upc(string? value) =>
        string.Concat((value ?? string.Empty).Where(char.IsAsciiDigit));

    public static string ContentHash(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    public static string GlobalDiscId(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    public static string Format(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Contains(DiscFormatConstants.FourK, StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains(DiscFormatConstants.Uhd, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.FourK;
        }

        if (normalized.Contains(DiscFormatConstants.Dvd, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.Dvd;
        }

        if (normalized.Contains(DiscFormatConstants.BluRay, StringComparison.OrdinalIgnoreCase))
        {
            return DiscFormatConstants.BluRay;
        }

        return normalized;
    }
}
