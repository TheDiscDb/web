namespace TheDiscDb.UnitTests.Services.DiscLookup;

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Services.DiscLookup;
using TheDiscDb.UnitTests.Data.Changes;

public class DiscLookupQueryTests
{
    private const string AacsId = "A734E4BEE726B8943F2E8817E3956EFC5F786C8B";
    private const string DvdId = "91C2EB717C4323D8807C01BA79011A6B";

    [Test]
    public async Task LookupAsync_KnownAacsId_ReturnsDiscWithTitlesAndItem()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.GlobalDiscId = AacsId;
        seed.Disc.ContentHash = "AAAA1111BBBB2222CCCC3333DDDD4444";
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, AacsId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GlobalDiscId).IsEqualTo(AacsId);
        await Assert.That(result.Format).IsEqualTo("Blu-ray");
        await Assert.That(result.ContentHash).IsEqualTo("AAAA1111BBBB2222CCCC3333DDDD4444");
        await Assert.That(result.Titles.Count).IsEqualTo(1);

        var title = result.Titles[0];
        await Assert.That(title.Index).IsEqualTo(ChangeTestSeed.TitleIndex);
        await Assert.That(title.SourceFile).IsEqualTo("00001.mpls");
        await Assert.That(title.SegmentMap).IsEqualTo("1,2,3");
        await Assert.That(title.Item).IsNotNull();
        await Assert.That(title.Item!.Title).IsEqualTo("Original Item Title");
        await Assert.That(title.Item.Type).IsEqualTo("movie");
        await Assert.That(title.Item.Season).IsEqualTo("1");
        await Assert.That(title.Item.Episode).IsEqualTo("3");
    }

    [Test]
    public async Task LookupAsync_IsCaseInsensitive()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.GlobalDiscId = AacsId;
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, AacsId.ToLowerInvariant());

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.GlobalDiscId).IsEqualTo(AacsId);
    }

    [Test]
    public async Task LookupAsync_UnknownId_ReturnsNull()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        ChangeTestSeed.Seed(db);
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, DvdId);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task LookupAsync_DvdId_Resolves()
    {
        using var db = ChangeTestSeed.CreateDbContext();
        var seed = ChangeTestSeed.Seed(db);
        seed.Disc.GlobalDiscId = DvdId;
        seed.Disc.Format = "DVD";
        await db.SaveChangesAsync();

        var result = await DiscLookupQuery.LookupAsync(db, DvdId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Format).IsEqualTo("DVD");
    }

    [Test]
    public async Task IsValidDiscId_ValidatesHexLengths()
    {
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId)).IsTrue();     // 40 hex
        await Assert.That(DiscLookupQuery.IsValidDiscId(DvdId)).IsTrue();      // 32 hex
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId.ToLowerInvariant())).IsTrue();
        await Assert.That(DiscLookupQuery.IsValidDiscId("not-hex")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId("")).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId(null)).IsFalse();
        await Assert.That(DiscLookupQuery.IsValidDiscId(AacsId + "AA")).IsFalse(); // 42
    }
}
