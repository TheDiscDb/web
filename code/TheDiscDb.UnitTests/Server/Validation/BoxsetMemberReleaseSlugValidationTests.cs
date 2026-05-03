using TheDiscDb.Validation.Boxset;
using TheDiscDb.Web.Data;

namespace TheDiscDb.UnitTests.Server.Validation;

public class BoxsetMemberReleaseSlugValidationTests
{
    private readonly BoxsetMemberReleaseSlugValidation validator = new();

    [Test]
    public async Task Validate_EmptyMembers_Passes()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_EmptyBoxsetSlug_Passes()
    {
        var boxset = new UserContributionBoxset { Slug = "" };
        boxset.Members.Add(new UserContributionBoxsetMember
        {
            Disc = new UserContributionDisc
            {
                Name = "Disc 1",
                UserContribution = new UserContribution { ReleaseSlug = "anything", Title = "X" }
            }
        });

        var result = await validator.Validate(boxset, CancellationToken.None);

        // Slug-empty case is owned by BoxsetSlugValidation; this validator should
        // not pile on a confusing second error.
        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_AllMatch_Passes()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };
        boxset.Members.Add(MakeContribMember("Matrix", "the-matrix-trilogy-2018"));
        boxset.Members.Add(MakeContribMember("Reloaded", "the-matrix-trilogy-2018"));

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_AllMatchCaseInsensitive_Passes()
    {
        var boxset = new UserContributionBoxset { Slug = "The-Matrix-Trilogy-2018" };
        boxset.Members.Add(MakeContribMember("Matrix", "the-matrix-trilogy-2018"));

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_OneMismatch_FailsAndNamesIt()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };
        boxset.Members.Add(MakeContribMember("Matrix", "the-matrix-trilogy-2018"));
        boxset.Members.Add(MakeContribMember("Reloaded", "2003-blu-ray"));

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Errors.First().Message).Contains("Reloaded");
        await Assert.That(result.Errors.First().Message).Contains("2003-blu-ray");
        await Assert.That(result.Errors.First().Message).Contains("the-matrix-trilogy-2018");
    }

    [Test]
    public async Task Validate_ExistingDiscPathMismatch_Fails()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };
        boxset.Members.Add(new UserContributionBoxsetMember
        {
            ExistingDiscPath = "Movie/603/2003-blu-ray/1",
            ExistingDiscName = "Reloaded",
        });

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Errors.First().Message).Contains("Reloaded");
        await Assert.That(result.Errors.First().Message).Contains("2003-blu-ray");
    }

    [Test]
    public async Task Validate_ExistingDiscPathMatch_Passes()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };
        boxset.Members.Add(new UserContributionBoxsetMember
        {
            ExistingDiscPath = "Movie/603/the-matrix-trilogy-2018/1",
            ExistingDiscName = "Matrix",
        });

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Validate_MalformedExistingDiscPath_Fails()
    {
        var boxset = new UserContributionBoxset { Slug = "the-matrix-trilogy-2018" };
        boxset.Members.Add(new UserContributionBoxsetMember
        {
            ExistingDiscPath = "this-is-not-a-valid-path",
            ExistingDiscName = "Bad",
        });

        var result = await validator.Validate(boxset, CancellationToken.None);

        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Errors.First().Message).Contains("invalid disc path");
        await Assert.That(result.Errors.First().Message).Contains("Bad");
    }

    private static UserContributionBoxsetMember MakeContribMember(string discName, string releaseSlug) => new()
    {
        Disc = new UserContributionDisc
        {
            Name = discName,
            UserContribution = new UserContribution
            {
                ReleaseSlug = releaseSlug,
                Title = discName,
            },
        },
    };
}
