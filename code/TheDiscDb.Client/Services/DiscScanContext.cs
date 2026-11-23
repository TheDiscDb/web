namespace TheDiscDb.Client.Services;

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using StrawberryShake;
using TheDiscDb.Core.DiscHash;

public sealed record DiscReleaseMatch(string Title, int Year, string Url, string? Upc, IReadOnlyList<string> Discs, string? ImageUrl = null);
public sealed record DiscTitleMatch(string Title, int Year, string? MediaType, string? ExternalId, string Slug, IReadOnlyList<DiscReleaseMatch> Releases)
{
    public bool CanContribute => new ContributionContinuation(this.MediaType ?? "", this.ExternalId ?? "").IsValid;
}

public sealed record DiscScanContext(DiscScanDraft Draft, IReadOnlyList<DiscTitleMatch> Titles);

public sealed class DiscScanContextLoader(
    BrowserScanStorage storage,
    HttpClient http,
    AuthenticationStateProvider authentication,
    GetDiscDetailByContentHashQuery query)
{
    private DiscScanContext? cached;
    private string? cachedLocalId;

    public async Task<DiscScanContext> LoadAsync(string hash, CancellationToken cancellationToken)
    {
        var local = await storage.GetScanAsync(hash, cancellationToken);
        if (local is not null && this.cachedLocalId == local.Id && this.cached is not null)
        {
            return this.cached;
        }

        DiscScanDraft? draft = local?.Draft;
        if (draft is null && (await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated == true)
        {
            using var response = await http.GetAsync(DraftUrl(hash), cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
                draft = await response.Content.ReadFromJsonAsync<DiscScanDraft>(cancellationToken);
            }
        }

        if (draft is null)
        {
            throw new InvalidOperationException("This browser has no saved scan. Sign in to use an account-saved scan, or scan the disc again.");
        }

        var result = await query.ExecuteAsync(hash.ToUpperInvariant(), templates: null, cancellationToken: cancellationToken);
        if (!result.IsSuccessResult())
        {
            throw new InvalidOperationException("Could not check the database for matching releases. Please retry.");
        }

        var titles = result.Data?.MediaItems?.Nodes?.Select(media => new DiscTitleMatch(
            media.Title ?? "Unknown title", media.Year, media.Type?.ToLowerInvariant(), media.Externalids?.Tmdb,
            media.Slug ?? "", media.Releases
                .Where(release => release.Discs.Any(disc => string.Equals(disc.ContentHash, hash, StringComparison.OrdinalIgnoreCase)))
                .Select(release => new DiscReleaseMatch(
                    release.Title ?? "Untitled release", release.Year,
                    $"/{Uri.EscapeDataString(media.Type?.ToLowerInvariant() ?? "")}/{Uri.EscapeDataString(media.Slug ?? "")}/releases/{Uri.EscapeDataString(release.Slug ?? "")}",
                    release.Upc, release.Discs
                        .Where(disc => string.Equals(disc.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                        .Select(disc => $"{disc.Name ?? $"Disc {disc.Index}"} - {disc.Format}").ToArray(), release.ImageUrl))
                .ToArray()))
            .Where(title => title.Releases.Count > 0).ToArray() ?? [];
        if (draft.IsExistingDiscReference && titles.Length == 0)
        {
            throw new InvalidOperationException("This disc is no longer in the database. Scan the disc again to continue.");
        }

        var context = new DiscScanContext(draft, titles);
        this.cached = context;
        this.cachedLocalId = local?.Id;
        return context;
    }

    public static string DraftUrl(string hash) => $"api/contribute/disc/{DiscScanDraft.EncodeHashForRoute(hash)}/scan";
}