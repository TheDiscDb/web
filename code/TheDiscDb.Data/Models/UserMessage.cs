namespace TheDiscDb.Web.Data;

using System;

public class UserMessage
{
    public int Id { get; set; }
    public int? ContributionId { get; set; }
    public int? BoxsetId { get; set; }
    public UserContribution? Contribution { get; set; }
    public UserContributionBoxset? Boxset { get; set; }
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public UserMessageType Type { get; set; }
}
