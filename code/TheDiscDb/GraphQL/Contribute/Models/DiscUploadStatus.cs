namespace TheDiscDb.GraphQL.Contribute.Models;

public record DiscUploadStatus(bool LogsUploaded, bool ManifestUploaded, string? LogUploadError);
