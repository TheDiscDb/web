using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class Messages : ComponentBase
{
    private (string Text, string Url)[] breadcrumbItems = [];

    protected override void OnInitialized()
    {
        breadcrumbItems = [
            BreadCrumbHelper.GetRootContributionLink()
        ];
    }
}
