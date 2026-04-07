using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Contribute;

public partial class EngramDetails : ComponentBase
{
    [Parameter]
    public string? ReleaseId { get; set; }

    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    private List<EngramSubmission> Submissions { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        if (!string.IsNullOrEmpty(ReleaseId))
        {
            await using var db = await DbFactory.CreateDbContextAsync();
            Submissions = await db.EngramSubmissions
                .Include(s => s.Titles)
                .Where(s => s.ReleaseId == ReleaseId)
                .OrderByDescending(s => s.ReceivedAt)
                .ToListAsync();
        }
    }

    private static string FormatDuration(int? seconds)
    {
        if (seconds == null) return "";
        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.Hours > 0 ? $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s" : $"{ts.Minutes}m {ts.Seconds}s";
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes == null) return "";
        return bytes.Value switch
        {
            >= 1_073_741_824 => $"{bytes.Value / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes.Value / 1_048_576.0:F1} MB",
            _ => $"{bytes.Value / 1024.0:F0} KB"
        };
    }
}
