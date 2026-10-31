namespace TheDiscDb.Contributions.OpticalDiscManifest;

public sealed record UsefulContentAggregate(
    int MeasuredDiscs, int CompleteKnownUsefulDiscs, int NoUsefulReferences, int AcquisitionReview,
    int NotComparable, int NotMeasured, int KnownUseful, int Available, int RequiredUnresolved,
    int UnclassifiedReferenceRows, int UnclassifiedScanChoices, int NamedMappingReviewChoices, int CaptionReviewChoices)
{
    public string Summary => $"Known useful choices {Available}/{KnownUseful}; required choices unresolved {RequiredUnresolved}; "
        + $"all known useful available on {CompleteKnownUsefulDiscs}/{MeasuredDiscs} measured labeled discs. "
        + $"No useful references {NoUsefulReferences}; acquisition review {AcquisitionReview}; "
        + $"not comparable {NotComparable}; not measured {NotMeasured}. "
        + $"Unclassified reference/scan choices {UnclassifiedReferenceRows}/{UnclassifiedScanChoices}; "
        + $"named stream mapping review {NamedMappingReviewChoices}; caption review {CaptionReviewChoices}.";

    public static UsefulContentAggregate Create(IEnumerable<UsefulContentComparison?> comparisons)
    {
        var all = comparisons.ToArray();
        var current = all.OfType<UsefulContentComparison>()
            .Where(item => item.Version == UsefulContentComparison.CurrentVersion).ToArray();
        return new(
            current.Count(item => item.Status == UsefulContentStatus.Measured),
            current.Count(item => item.AllKnownUsefulAvailable),
            current.Count(item => item.Status == UsefulContentStatus.NoUsefulReferences),
            current.Count(item => item.Status == UsefulContentStatus.AcquisitionReview),
            current.Count(item => item.Status == UsefulContentStatus.NotComparable),
            all.Length - current.Length,
            current.Sum(item => item.KnownUseful), current.Sum(item => item.Available),
            current.Sum(item => item.RequiredUnresolved), current.Sum(item => item.UnclassifiedReferenceRows),
            current.Sum(item => item.UnclassifiedScanChoices),
            current.Sum(item => item.Choices.Count(choice => choice.Streams?.NamedLabelsNotSemanticallyVerified > 0)),
            current.Sum(item => item.Choices.Count(choice => choice.Streams is
                { ReferenceClosedCaptions: true, DeclaredClosedCaptions: false })));
    }
}
