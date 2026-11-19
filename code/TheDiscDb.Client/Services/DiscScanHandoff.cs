namespace TheDiscDb.Client.Services;

using System.Net;
using System.Net.Http.Json;
using TheDiscDb.Core.DiscHash;

public sealed class DiscScanHandoff(BrowserScanStorage storage, HttpClient http)
{
    public async Task<string> PrepareAsync(ContributionContinuation continuation, CancellationToken cancellationToken = default)
    {
        if (!continuation.IsValid)
        {
            throw new InvalidOperationException("Choose a movie or series before continuing.");
        }
        if (string.IsNullOrWhiteSpace(continuation.DiscHash))
        {
            return continuation.DetailsUrl;
        }

        string uri = DiscScanContextLoader.DraftUrl(continuation.DiscHash);
        var local = await storage.GetScanAsync(continuation.DiscHash, cancellationToken);
        if (local is not null && local.Id != continuation.LocalScanId)
        {
            throw new InvalidOperationException("This disc has a newer scan. Return to title selection to continue with it.");
        }

        using var response = await http.GetAsync(uri, cancellationToken);
        DiscScanDraft? saved = null;
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
            saved = await response.Content.ReadFromJsonAsync<DiscScanDraft>(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(saved?.ContributionId))
        {
            return $"/contribution/{Uri.EscapeDataString(saved.ContributionId)}";
        }

        if (saved is not null && local is not null
            && !string.Equals(saved.Fingerprint, local.Draft.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A different saved scan already uses this disc hash. Return to scanning before continuing.");
        }

        if (saved is null)
        {
            if (local is null)
            {
                throw new InvalidOperationException("The scan is no longer saved in this browser or account. Scan the disc again to continue.");
            }

            using var upload = await http.PostAsJsonAsync(uri, local.Draft, cancellationToken);
            upload.EnsureSuccessStatusCode();
        }

        if (local is not null)
        {
            await storage.RemoveScanAsync(continuation.DiscHash, local.Id, cancellationToken);
        }
        return continuation.DetailsUrl;
    }
}