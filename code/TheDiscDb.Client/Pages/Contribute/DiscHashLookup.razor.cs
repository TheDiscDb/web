using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Client.Services;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class DiscHashLookup
{
    [Parameter]
    public string? DiscHash { get; set; }

    [Inject]
    private GetDiscDetailByContentHashQuery ContentHashQuery { get; set; } = default!;

    [Inject]
    private HttpClient HttpClient { get; set; } = default!;

    private DiscScanDraft? draft;
    private List<ReleaseMatch> matches = [];
    private string? loadError;
    private bool isLoading = true;
    private string lookupHash = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        this.isLoading = true;
        this.loadError = null;
        this.matches = [];
        this.draft = null;

        if (!DiscScanDraft.TryDecodeHashFromRoute(this.DiscHash ?? string.Empty, out string decodedHash))
        {
            this.loadError = "The disc hash URL is invalid. Scan the disc again to continue.";
            this.isLoading = false;
            return;
        }

        this.lookupHash = decodedHash;

        if (string.IsNullOrWhiteSpace(this.lookupHash))
        {
            this.loadError = "The disc hash is missing.";
            this.isLoading = false;
            return;
        }

        try
        {
            string draftUri = $"api/contribute/disc/{DiscScanDraft.EncodeHashForRoute(this.lookupHash)}/scan";
            this.draft = await this.HttpClient.GetFromJsonAsync<DiscScanDraft>(draftUri, this.CancellationToken);
            if (this.draft is null)
            {
                this.loadError = "The saved scan could not be found. Scan the disc again to continue.";
                return;
            }

            var response = await this.ContentHashQuery.ExecuteAsync(
                this.draft.DiscHash,
                templates: null,
                cancellationToken: this.CancellationToken);
            if (!response.IsSuccessResult())
            {
                this.loadError = response.Errors?.FirstOrDefault()?.Message
                    ?? "Could not check the database for matching releases.";
                return;
            }

            this.matches = response.Data?.MediaItems?.Nodes?
                .SelectMany(media => media.Releases
                    .Where(release => release.Discs.Any(disc => string.Equals(
                        disc.ContentHash,
                        this.draft.DiscHash,
                        StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(release => release.Discs
                        .Where(disc => string.Equals(
                            disc.ContentHash,
                            this.draft.DiscHash,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(disc => new ReleaseMatch(
                            media.Title ?? "Unknown title",
                            media.Year,
                            release.Title ?? "Untitled release",
                            release.Year,
                            disc.Name ?? $"Disc {disc.Index}",
                            disc.Format ?? string.Empty,
                            release.Upc))))
                .ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            this.loadError = "The saved scan could not be found. Scan the disc again to continue.";
        }
        finally
        {
            this.isLoading = false;
        }
    }

    private string GetTmdbUrl(string mediaType)
        => $"/contribute/{mediaType}?discHash={Uri.EscapeDataString(this.lookupHash)}";

    private sealed record ReleaseMatch(
        string MediaTitle,
        int MediaYear,
        string ReleaseTitle,
        int ReleaseYear,
        string DiscName,
        string Format,
        string? Upc);
}
