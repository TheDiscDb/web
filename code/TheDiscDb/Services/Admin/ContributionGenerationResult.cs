namespace TheDiscDb.Services.Admin;

public sealed record ContributionGenerationResult(string ReleaseDirectory, IReadOnlyCollection<string> GeneratedFiles);
