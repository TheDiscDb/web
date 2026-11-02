using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using TheDiscDb.Services.Contributions;

namespace TheDiscDb.Components.Pages.Help;

public partial class Contributing
{
    [Inject]
    private IOptions<DiscScanOptions> DiscScanOptions { get; set; } = default!;

    private bool DiscFirstFlow => this.DiscScanOptions.Value.DiscFirstFlow;
}
