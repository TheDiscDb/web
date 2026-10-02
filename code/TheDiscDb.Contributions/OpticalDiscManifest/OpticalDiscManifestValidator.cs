using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace TheDiscDb.Contributions.OpticalDiscManifest;

/// <summary>
/// The outcome of validating and deserializing an uploaded Optical Disc Manifest.
/// </summary>
/// <param name="Document">The parsed manifest, or <see langword="null"/> when validation failed.</param>
/// <param name="Error">A single user-facing explanation of why the upload was rejected.</param>
public sealed record OpticalDiscManifestParseResult(OpticalDiscManifestDocument? Document, string? Error)
{
    public bool IsValid => Document is not null;

    public static OpticalDiscManifestParseResult Failure(string error) => new(null, error);

    public static OpticalDiscManifestParseResult Success(OpticalDiscManifestDocument document) => new(document, null);
}

/// <summary>
/// Validates uploaded Optical Disc Manifest documents against the v1 JSON schema.
/// </summary>
/// <remarks>
/// The schema is embedded in this assembly rather than read from the optical-disc-manifest
/// repository on disk. Pinning it means the server always validates against the exact revision of
/// the contract it was built to support, instead of whichever copy happens to be checked out
/// alongside it. Validation runs in-process, so no external tooling is involved.
/// </remarks>
public sealed class OpticalDiscManifestValidator
{
    private const string SchemaResourceName =
        "TheDiscDb.Contributions.OpticalDiscManifest.optical-disc-manifest.v1.schema.json";

    private const int MaxReportedErrors = 5;

    private static readonly Lazy<JsonSchema> Schema = new(LoadSchema, isThreadSafe: true);

    // Applicator errors are the noisy roll-ups an "anyOf"/"contains" keyword produces for every
    // branch it tried. They bury the one error that actually explains the problem.
    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        IncludeApplicatorErrors = false
    };

    /// <summary>
    /// Validates <paramref name="json"/> against the ODM v1 schema and deserializes it on success.
    /// </summary>
    public OpticalDiscManifestParseResult Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return OpticalDiscManifestParseResult.Failure("The manifest file is empty.");
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return OpticalDiscManifestParseResult.Failure($"The manifest is not valid JSON: {ex.Message}");
        }

        using (parsed)
        {
            EvaluationResults results = Schema.Value.Evaluate(parsed.RootElement, Options);
            if (!results.IsValid)
            {
                return OpticalDiscManifestParseResult.Failure(DescribeFailure(results));
            }
        }

        OpticalDiscManifestDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<OpticalDiscManifestDocument>(
                json, OpticalDiscManifestDocument.SerializerOptions);
        }
        catch (JsonException ex)
        {
            return OpticalDiscManifestParseResult.Failure($"The manifest could not be read: {ex.Message}");
        }

        if (document is null)
        {
            return OpticalDiscManifestParseResult.Failure("The manifest could not be read.");
        }

        // The schema pins schemaVersion to 1, so this only trips if the embedded schema is ever
        // widened to cover more versions than the mapper below actually understands.
        if (document.SchemaVersion != OpticalDiscManifestDocument.SupportedSchemaVersion)
        {
            return OpticalDiscManifestParseResult.Failure(
                $"Unsupported manifest schema version {document.SchemaVersion}. " +
                $"Only version {OpticalDiscManifestDocument.SupportedSchemaVersion} is supported.");
        }

        if (document.Disc is null)
        {
            return OpticalDiscManifestParseResult.Failure("The manifest does not describe a disc.");
        }

        return OpticalDiscManifestParseResult.Success(document);
    }

    private static string DescribeFailure(EvaluationResults results)
    {
        var messages = Flatten(results)
            .Where(result => !result.IsValid && result.Errors is { Count: > 0 })
            .SelectMany(result => result.Errors!.Values
                .Select(message => Describe(result.InstanceLocation.ToString(), message)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (messages.Count == 0)
        {
            return "The manifest does not conform to the Optical Disc Manifest v1 schema.";
        }

        var reported = string.Join(" ", messages.Take(MaxReportedErrors));
        if (messages.Count > MaxReportedErrors)
        {
            reported += $" (and {messages.Count - MaxReportedErrors} more problems)";
        }

        return $"The manifest does not conform to the Optical Disc Manifest v1 schema. {reported}";
    }

    private static string Describe(string location, string message)
        => string.IsNullOrEmpty(location) ? $"{message}." : $"At '{location}': {message}.";

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults results)
    {
        yield return results;

        if (results.Details is null)
        {
            yield break;
        }

        foreach (var child in results.Details)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static JsonSchema LoadSchema()
    {
        var assembly = typeof(OpticalDiscManifestValidator).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(SchemaResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded Optical Disc Manifest schema '{SchemaResourceName}' is missing from {assembly.GetName().Name}.");

        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
