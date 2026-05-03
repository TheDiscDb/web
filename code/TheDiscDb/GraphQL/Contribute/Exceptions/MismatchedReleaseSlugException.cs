namespace TheDiscDb.GraphQL.Contribute.Exceptions;

/// <summary>
/// Thrown when an attempt is made to add a disc to a boxset whose release slug
/// does not match the boxset's slug. The current data layout requires every
/// member's release directory to be named exactly <c>boxset.Slug</c> so the
/// import-time resolver can locate it; see
/// <c>DataImportItemFactory.FindBoxsetDisc</c>.
/// </summary>
public class MismatchedReleaseSlugException : Exception
{
    public MismatchedReleaseSlugException(string boxsetSlug, string offendingReleaseSlug, string contributionTitle)
        : base($"Cannot add '{contributionTitle}' to this boxset. Its release slug is '{offendingReleaseSlug}' but the boxset's slug is '{boxsetSlug}'. Every contribution added to a boxset must use the boxset's slug as its release slug.")
    {
        BoxsetSlug = boxsetSlug;
        OffendingReleaseSlug = offendingReleaseSlug;
        ContributionTitle = contributionTitle;
    }

    public string BoxsetSlug { get; }
    public string OffendingReleaseSlug { get; }
    public string ContributionTitle { get; }
}
