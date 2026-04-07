namespace TheDiscDb.Web.Data;

using System;

public class ContributionHistory
{
    public int Id { get; set; }
    public int ContributionId { get; set; }
    public DateTimeOffset TimeStamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ContributionHistoryType Type { get; set; }
}
