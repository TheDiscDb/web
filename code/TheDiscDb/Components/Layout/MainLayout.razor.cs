using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace TheDiscDb.Components.Layout;

public partial class MainLayout : LayoutComponentBase
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private bool UsePopupShell => IsNamingPopupRoute();

    private bool IsNamingPopupRoute()
    {
        var absoluteUri = this.NavigationManager.ToAbsoluteUri(this.NavigationManager.Uri);
        var path = absoluteUri.AbsolutePath;

        if (!path.StartsWith("/contribution/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var namingIndex = path.IndexOf("/naming", StringComparison.OrdinalIgnoreCase);
        if (namingIndex < 0)
        {
            return false;
        }

        // Must end with /naming or /naming/{itemId}
        var suffix = path[namingIndex..];
        var isNamingRoute = suffix.Equals("/naming", StringComparison.OrdinalIgnoreCase)
            || (suffix.StartsWith("/naming/", StringComparison.OrdinalIgnoreCase) && !suffix["/naming/".Length..].Contains('/'));

        if (!isNamingRoute)
        {
            return false;
        }

        var query = QueryHelpers.ParseQuery(absoluteUri.Query);
        return query.TryGetValue("popup", out var popupValue)
            && string.Equals(popupValue.FirstOrDefault(), "1", StringComparison.Ordinal);
    }
}
