using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;

namespace TheDiscDb.Client.Pages.Contribute;

public partial class ScanDisc
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IConfiguration Configuration { get; set; } = default!;

    protected override void OnInitialized()
    {
        if (!this.Configuration.GetValue<bool>("Contributions:DiscFirstFlow"))
        {
            this.NavigationManager.NavigateTo("/contribute", replace: true);
        }
    }
}
