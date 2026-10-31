using System.Globalization;

namespace MakeMkv;

public static class LanguageCodes
{
    private static readonly IReadOnlyDictionary<string, string> Codes = CreateCodes();

    public static string? Canonicalize(string? value)
    {
        string? code = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(code) || code == "und"
            ? null
            : Codes.GetValueOrDefault(code, code);
    }

    private static Dictionary<string, string> CreateCodes()
    {
        var codes = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(culture => culture.TwoLetterISOLanguageName.Length == 2
                && culture.ThreeLetterISOLanguageName.Length == 3)
            .GroupBy(culture => culture.TwoLetterISOLanguageName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().ThreeLetterISOLanguageName.ToLowerInvariant(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in new Dictionary<string, string>
        {
            ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["bur"] = "mya",
            ["chi"] = "zho", ["cze"] = "ces", ["dut"] = "nld", ["fre"] = "fra",
            ["geo"] = "kat", ["ger"] = "deu", ["gre"] = "ell", ["ice"] = "isl",
            ["mac"] = "mkd", ["mao"] = "mri", ["may"] = "msa", ["per"] = "fas",
            ["rum"] = "ron", ["slo"] = "slk", ["tib"] = "bod", ["wel"] = "cym"
        })
        {
            codes[pair.Key] = pair.Value;
        }

        return codes;
    }
}
