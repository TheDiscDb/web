namespace TheDiscDb.Web.Data;

public interface IContributionDisplay
{
    string EncodedId { get; }
    string? Title { get; }
    string? Year { get; }
    string? ReleaseTitle { get; }
}
