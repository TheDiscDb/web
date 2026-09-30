using System.Text.Json;
using Json.Schema;
using TheDiscDb.OpticalDiscManifest.Models;

namespace TheDiscDb.OpticalDiscManifest.Validation;

public sealed class OpticalDiscManifestSchemaValidator
{
    private const string ResourceName =
        "TheDiscDb.OpticalDiscManifest.Schema.optical-disc-manifest-v1.schema.json";

    private readonly JsonSchema schema;

    public OpticalDiscManifestSchemaValidator()
    {
        using var stream = typeof(OpticalDiscManifestSchemaValidator)
            .Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{ResourceName}' was not found.");
        using var schemaDocument = JsonDocument.Parse(stream);
        schema = JsonSchema.Build(
            schemaDocument.RootElement.Clone(),
            new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry(),
            });
    }

    public ManifestValidationResult Validate(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var results = schema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
            });

        if (results.IsValid)
        {
            return new ManifestValidationResult(true, Array.Empty<string>());
        }

        var errors = (results.Details ?? [])
            .Where(detail => !detail.IsValid)
            .SelectMany(detail => detail.Errors?.Select(error =>
                $"{detail.InstanceLocation}: {error.Value}") ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ManifestValidationResult(
            false,
            errors.Length > 0 ? errors : ["Manifest does not satisfy the bundled JSON Schema."]);
    }
}
