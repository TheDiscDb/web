using Microsoft.AspNetCore.Components;
using TheDiscDb.InputModels;

namespace TheDiscDb.Components.Controls;

public partial class ExternalProviderLinks : ComponentBase
{
    [Parameter]
    public ExternalIds? ExternalIds { get; set; }

    [Parameter]
    public int Width { get; set; } = 20;

    private List<(ExternalProviderDefinition Provider, string Url)> Links =>
        this.ExternalIds == null
            ? []
            : ExternalProviderCatalog.Providers
                .Select(p => (Provider: p, Url: p.BuildUrl(ExternalProviderCatalog.GetStoredId(this.ExternalIds, p.Id))))
                .Where(x => x.Url != null)
                .Select(x => (x.Provider, Url: x.Url!))
                .ToList();
}
