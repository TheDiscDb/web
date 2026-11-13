namespace TheDiscDb.UnitTests.OpticalDiscManifests;

using System.Text.Json;
using MakeMkv;
using TheDiscDb.Contributions.OpticalDiscManifest;

public class ManifestStreamCapabilityTests
{
    [Test]
    [Arguments(" en ", "eng")]
    [Arguments("fr", "fra")]
    [Arguments("FRE", "fra")]
    [Arguments("ger", "deu")]
    [Arguments("de", "deu")]
    [Arguments("chi", "zho")]
    [Arguments("jpn", "jpn")]
    [Arguments("und", null)]
    [Arguments("unknown-code", "unknown-code")]
    public async Task LanguageCodes_CanonicalizeIsoAliasesWithoutInventingUnknownLanguages(string input, string? expected)
    {
        await Assert.That(LanguageCodes.Canonicalize(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task Mapper_PreservesNativeIdentityAndAddsDvdClosedCaptionTrackLast()
    {
        const string json = """
            {"disc":{"format":"dvd","titles":[{"streams":[
              {"type":"video","codec":"MPEG-2","line21ClosedCaptionFields":[1]},
              {"type":"audio","codec":"DTS","language":"de","category":"primary-audio",
               "pid":4352,"codingTypeCode":130,"formatCode":6,"rateCode":1},
              {"type":"subtitle","codec":"VobSub","language":"fr"}
            ]}]}}
            """;
        var manifest = JsonSerializer.Deserialize<OpticalDiscManifestDocument>(
            json, OpticalDiscManifestDocument.SerializerOptions)!;
        var info = OpticalDiscManifestMapper.ToDiscInfo(manifest);
        var title = info.Titles.Single();
        var audio = title.Segments.Single(stream => stream.Type == "Audio");
        var captions = title.Segments[^1];

        await Assert.That(info.SourceKind).IsEqualTo("optical-disc-manifest");
        await Assert.That(title.Segments.Count).IsEqualTo(4);
        await Assert.That(captions.Index).IsEqualTo(3);
        await Assert.That(captions.Type).IsEqualTo("Subtitles");
        await Assert.That(captions.Name).IsEqualTo("Closed Captions");
        await Assert.That(captions.LanguageCode).IsEqualTo("eng");
        await Assert.That(audio.Pid).IsEqualTo(4352);
        await Assert.That(audio.CodingTypeCode).IsEqualTo(130);
        await Assert.That(audio.FormatCode).IsEqualTo(6);
        await Assert.That(audio.RateCode).IsEqualTo(1);
        await Assert.That(audio.Category).IsEqualTo("primary-audio");
        await Assert.That(audio.SourceCodec).IsEqualTo("DTS");
        await Assert.That(audio.LanguageCode).IsEqualTo("deu");
        await Assert.That(manifest.Disc!.Titles[0].Streams[1].Language).IsEqualTo("de");
    }

    [Test]
    public async Task Mapper_AbsentCaptionDeclarationsAddNoClosedCaptionTrack()
    {
        var document = new OpticalDiscManifestDocument
        {
            Disc = new OpticalDiscManifestDisc
            {
                Format = "dvd",
                Titles = { new OpticalDiscManifestTitle
                {
                    Streams = { new OpticalDiscManifestStream { Type = "video", Codec = "MPEG-2" } }
                } }
            }
        };

        await Assert.That(OpticalDiscManifestMapper.ToDiscInfo(document).Titles[0].Segments.Count).IsEqualTo(1);
    }
}
