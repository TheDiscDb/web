using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Components.Pages;

public partial class ContributionStats : ComponentBase, IDisposable
{
    [Inject]
    private CacheHelper CacheHelper { get; set; } = null!;

    [Inject]
    private ILogger<ContributionStats> Logger { get; set; } = null!;

    private readonly CancellationTokenSource cts = new();
    private List<ContributionStatsPoint>? chartData;
    private int totalContributions;
    private DateTime? firstContributionDate;
    private DateTime? latestContributionDate;
    private string? errorMessage;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var dates = await this.CacheHelper.GetContributionDatesAsync(this.cts.Token);

            chartData = BuildCumulativeSeries(dates);
            totalContributions = dates.Count;

            if (chartData.Count > 0)
            {
                firstContributionDate = chartData[0].Date;
                latestContributionDate = chartData[^1].Date;
            }
        }
        catch (OperationCanceledException) when (this.cts.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            this.Logger.LogError(exception, "Unable to load contribution statistics.");
            errorMessage = "Unable to load contribution statistics. Please try again later.";
            chartData = [];
        }
    }

    internal static List<ContributionStatsPoint> BuildCumulativeSeries(IEnumerable<DateTimeOffset> dates)
    {
        var contributionsByDate = dates
            .GroupBy(date => date.UtcDateTime.Date)
            .ToDictionary(group => group.Key, group => group.Count());

        if (contributionsByDate.Count == 0)
        {
            return [];
        }

        var firstDate = contributionsByDate.Keys.Min();
        var lastDate = contributionsByDate.Keys.Max();
        var result = new List<ContributionStatsPoint>();
        var total = 0;

        for (var date = firstDate; ; date = date.AddDays(1))
        {
            total += contributionsByDate.GetValueOrDefault(date);
            result.Add(new ContributionStatsPoint(date, total));

            if (date == lastDate)
            {
                break;
            }
        }

        return result;
    }

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
    }

    internal sealed record ContributionStatsPoint(DateTime Date, int Total);
}
