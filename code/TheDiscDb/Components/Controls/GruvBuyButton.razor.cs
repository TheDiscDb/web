namespace TheDiscDb.Components.Controls;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Affiliate;

public partial class GruvBuyButton : ComponentBase, IDisposable
{
    [Parameter]
    public string? MediaItemSlug { get; set; }

    [Parameter]
    public string? BoxsetSlug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    /// <summary>
    /// CJ sub-id reported to Commission Junction. Used in CJ reports to attribute clicks to a
    /// specific surface (e.g. <c>release-detail</c>, <c>release-list</c>, <c>boxset-detail</c>).
    /// Optional — when null the <c>sid</c> query parameter is omitted from the CJ URL.
    /// </summary>
    [Parameter]
    public string? Sid { get; set; }

    [Parameter]
    public int Width { get; set; } = 17;

    [Inject]
    public IGruvLinkLookup Lookup { get; set; } = null!;

    [Inject]
    public AffiliateLinkService Decorator { get; set; } = null!;

    private string? AffiliateUrl { get; set; }

    // Component-owned cancellation so that rapid parameter changes (e.g. Blazor route reuse
    // across different releases) cancel an in-flight lookup before its result could stomp the
    // newer parameters' result onto AffiliateUrl.
    private CancellationTokenSource? cts;

    protected override async Task OnParametersSetAsync()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();

        this.AffiliateUrl = null;
        if (string.IsNullOrEmpty(this.ReleaseSlug))
        {
            return;
        }

        this.cts = new CancellationTokenSource();
        var token = this.cts.Token;
        var capturedMedia = this.MediaItemSlug;
        var capturedBoxset = this.BoxsetSlug;
        var capturedRelease = this.ReleaseSlug;

        string? providerUrl;
        try
        {
            var row = await this.Lookup.GetAsync(capturedMedia, capturedBoxset, capturedRelease, token);
            providerUrl = row?.ProviderUrl;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Only commit if the parameters haven't changed since we issued the lookup.
        if (token.IsCancellationRequested
            || !string.Equals(capturedMedia, this.MediaItemSlug, StringComparison.Ordinal)
            || !string.Equals(capturedBoxset, this.BoxsetSlug, StringComparison.Ordinal)
            || !string.Equals(capturedRelease, this.ReleaseSlug, StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrEmpty(providerUrl))
        {
            this.AffiliateUrl = null;
            return;
        }

        var decorated = this.Decorator.Decorate(providerUrl, this.Sid);
        this.AffiliateUrl = string.IsNullOrEmpty(decorated) ? null : decorated;
    }

    public void Dispose()
    {
        this.cts?.Cancel();
        this.cts?.Dispose();
        this.cts = null;
    }
}
