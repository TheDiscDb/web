using TheDiscDb.Contributions.OpticalDiscManifest;

namespace TheDiscDb.UnitTests.OpticalDiscManifests;

public class MakeMkvMinimumLengthEvidenceTests
{
    [Test]
    [Arguments("1", MakeMkvMinimumLengthStatus.NonZero)]
    [Arguments("0", MakeMkvMinimumLengthStatus.Zero)]
    public async Task Analyze_UsesFinalCsvParameterAsMinimumLength(string threshold, MakeMkvMinimumLengthStatus expected)
    {
        string log = $"MSG:3025,0,2,\"Durée du titre, filtrée\",\"Modèle avec, virgule\",\"0:01:30\",\"{threshold}\"";

        var evidence = MakeMkvMinimumLengthAnalyzer.Analyze(log);

        await Assert.That(evidence.Status).IsEqualTo(expected);
        await Assert.That(evidence.ThresholdSeconds).IsEqualTo(long.Parse(threshold));
    }

    [Test]
    public async Task Analyze_NoSkipMessageIsUnknown_NotEvidenceOfZero()
    {
        var evidence = MakeMkvMinimumLengthAnalyzer.Analyze("MSG:1005,0,1,\"MakeMKV v1.17.8\",\"MakeMKV v%1\",\"1.17.8\"");

        await Assert.That(evidence.Status).IsEqualTo(MakeMkvMinimumLengthStatus.Unknown);
        await Assert.That(evidence.ThresholdSeconds).IsNull();
        await Assert.That(evidence.ExcludesReference).IsFalse();
    }

    [Test]
    [Arguments("MSG:3025,0,2,\"title\",\"template\",\"0:01:00\",\"not-a-number\"")]
    [Arguments("MSG:3025,0,2,\"title\",\"template\",\"0:01:00\"")]
    public async Task Analyze_MalformedEvidenceIsUnknownAndNeverExcludes(string log)
    {
        var evidence = MakeMkvMinimumLengthAnalyzer.Analyze(log);

        await Assert.That(evidence.Status).IsEqualTo(MakeMkvMinimumLengthStatus.Malformed);
        await Assert.That(evidence.ExcludesReference).IsFalse();
    }

    [Test]
    public async Task Analyze_ConflictingThresholdsAreUnknownAndNeverExclude()
    {
        const string log = """
            MSG:3025,0,2,"title","template","0:00:30","0"
            MSG:3025,0,2,"title","template","0:01:00","1"
            """;

        var evidence = MakeMkvMinimumLengthAnalyzer.Analyze(log);

        await Assert.That(evidence.Status).IsEqualTo(MakeMkvMinimumLengthStatus.Conflicting);
        await Assert.That(evidence.ExcludesReference).IsFalse();
    }
}
