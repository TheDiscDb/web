using MakeMkv;
using TheDiscDb.Web.Data;

namespace TheDiscDb.GraphQL.Contribute.Models;

public class DiscLogs
{
    public DiscInfo? Info { get; set; }
    public UserContributionDisc? Disc { get; set; }
    public UserContribution? Contribution { get; set; }
}
