namespace TheDiscDb.Client.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.WebUtilities;
using TheDiscDb.Core.DiscHash;

public sealed record ContributionContinuation(
    string MediaType,
    string ExternalId,
    string? DiscHash = null,
    string? BoxsetId = null,
    bool FromMatches = false,
    string? LocalScanId = null)
{
    public bool IsValid => (this.MediaType is "movie" or "series")
        && ulong.TryParse(this.ExternalId, out ulong id) && id > 0;

    public string DetailsUrl => QueryHelpers.AddQueryString(
        $"/contribute/{this.MediaType}/{Uri.EscapeDataString(this.ExternalId)}",
        new Dictionary<string, string?>
        {
            ["discHash"] = this.DiscHash,
            ["boxsetId"] = this.BoxsetId,
            ["fromMatches"] = this.FromMatches ? "true" : null,
        });

    public static string SearchUrl(string mediaType, string? hash, string? boxsetId = null)
        => QueryHelpers.AddQueryString($"/contribute/{mediaType}", new Dictionary<string, string?>
        {
            ["discHash"] = hash,
            ["boxsetId"] = boxsetId,
        });

    public static string MatchesUrl(string hash, string? boxsetId = null)
        => QueryHelpers.AddQueryString($"/contribute/disc/{DiscScanDraft.EncodeHashForRoute(hash)}/releases",
            new Dictionary<string, string?> { ["boxsetId"] = boxsetId });

    public static string LoginUrl(string localUrl)
        => QueryHelpers.AddQueryString("/Account/Login", "ReturnUrl", localUrl);

    public static bool MatchesTitle(string? mediaType, string? externalId, string selectedMediaType, string selectedExternalId)
        => string.Equals(mediaType, selectedMediaType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(externalId, selectedExternalId, StringComparison.Ordinal);
}

public sealed class ContributionContinuationService(
    BrowserScanStorage storage,
    AuthenticationStateProvider authentication,
    NavigationManager navigation)
{
    public async Task ContinueAsync(ContributionContinuation continuation, CancellationToken cancellationToken)
    {
        if (!continuation.IsValid)
        {
            throw new InvalidOperationException("Choose a movie or series before continuing.");
        }

        LocalDiscScan? scan = string.IsNullOrWhiteSpace(continuation.DiscHash)
            ? null : await storage.GetScanAsync(continuation.DiscHash, cancellationToken);
        string id = Guid.NewGuid().ToString("N");
        await storage.SaveContinuationAsync(id, continuation with { LocalScanId = scan?.Id }, cancellationToken);
        string resumeUrl = $"/contribute/resume?draft={id}";
        bool signedIn = (await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true;
        navigation.NavigateTo(signedIn ? resumeUrl : ContributionContinuation.LoginUrl(resumeUrl), forceLoad: !signedIn);
    }
}