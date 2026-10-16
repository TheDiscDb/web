namespace TheDiscDb.Contributions.OpticalDiscManifest;

public sealed record IdentifySimilarityAggregate(
    int Measured, int NotComparable, int NotMeasured,
    double? Overall, double? ChoiceCoverage, double? RowPresentation, double? Ordering, double? Labeling,
    int MissingChoices, int ExtraChoices)
{
    public string Summary => Measured == 0 ? $"Not measured; {NotComparable} not comparable / {NotMeasured} not measured"
        : $"Mean per disc {Overall:F1}%; coverage {ChoiceCoverage:F1}%; presentation {RowPresentation:F1}%; "
            + $"ordering {Ordering:F1}%; labeling {Labeling:F1}%; {Measured} measured / "
            + $"{NotComparable} not comparable / {NotMeasured} not measured; missing {MissingChoices}, extra {ExtraChoices}";

    public static IdentifySimilarityAggregate Create(IEnumerable<IdentifyExperienceComparison?> experiences)
    {
        var all = experiences.ToArray();
        var scores = all.Where(e => e is { Version: IdentifyExperienceComparison.CurrentVersion,
                Status: IdentifyExperienceStatus.Match or IdentifyExperienceStatus.Mismatch })
            .Select(e => e!.Similarity).Where(s => s is { IsMeasured: true }).Select(s => s!).ToArray();
        int notComparable = all.Count(e => e is { Version: IdentifyExperienceComparison.CurrentVersion,
            Status: IdentifyExperienceStatus.NotComparable });
        return new(scores.Length, notComparable, all.Length - scores.Length - notComparable,
            scores.Length == 0 ? null : scores.Average(s => s.Overall),
            scores.Length == 0 ? null : scores.Average(s => s.ChoiceCoverage.Percent),
            scores.Length == 0 ? null : scores.Average(s => s.RowPresentation.Percent),
            scores.Length == 0 ? null : scores.Average(s => s.Ordering.Percent),
            scores.Length == 0 ? null : scores.Average(s => s.Labeling.Percent),
            scores.Sum(s => s.MissingChoices.Count), scores.Sum(s => s.ExtraChoices.Count));
    }
}
