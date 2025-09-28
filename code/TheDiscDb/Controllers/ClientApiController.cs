using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using Sqids;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Data.Import;
using TheDiscDb.Search;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web.Controllers;

[ApiController]
[Route("api")]
public class ClientApiController : ControllerBase
{
    private readonly ISearchService search;
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;
    private readonly UserManager<TheDiscDbUser> userManager;
    private readonly SqidsEncoder<int> idEncoder;
    private readonly IStaticAssetStore assetStore;

    public ClientApiController(ISearchService search, IDbContextFactory<SqlServerDataContext> dbContextFactory, UserManager<TheDiscDbUser> userManager, SqidsEncoder<int> idEncoder, IStaticAssetStore assetStore)
    {
        this.search = search ?? throw new System.ArgumentNullException(nameof(search));
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        this.idEncoder = idEncoder ?? throw new ArgumentNullException(nameof(idEncoder));
        this.assetStore = assetStore ?? throw new ArgumentNullException(nameof(assetStore));
    }

    [HttpGet("search")]
    public async Task<IEnumerable<SearchEntry>> Search(string s)
    {
        var results = await this.search.Search(s);
        return results;
    }

    [HttpGet("barcode")]
    public FileContentResult Barcode(string data, int width = 200)
    {
        var barcode = new Barcode.Barcode(data)
        {
            ShowLabel = true
        };

        var image = barcode.GenerateImage();
        image.Mutate(o => o.Resize(width, 0));
        using (var stream = new MemoryStream())
        {
            image.Save(stream, new PngEncoder());
            return File(stream.ToArray(), "image/png");
        }
    }

    [HttpPost("hash")]
    public HashResponse Hash([FromBody] HashRequest request)
    {
        var hash = request.Files.OrderBy(f => f.Name).CalculateHash();
        return new HashResponse { Hash = hash };
    }

    [HttpGet("search/external")]
    public Task<IEnumerable<ExternalSearchEntry>> ExternalSearch(string s)
    {
        throw new NotImplementedException();
    }

    [HttpPost("contribute/{contributionId}/addDisc/{discId}")]
    public async Task<ActionResult> AddDisc(string contributionId, string discId, [FromBody] string logs, CancellationToken cancellationToken)
    {
        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync();
        {
            int id = idEncoder.Decode(contributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contribution == null)
            {
                return this.NotFound(contributionId);
            }

            int realDiscId = idEncoder.Decode(discId).Single();
            var disc = contribution.Discs.FirstOrDefault(d => d.Id == realDiscId);

            if (disc == null)
            {
                return this.NotFound(discId);
            }

            //Save the logs in blob storage
            byte[] byteArray = Encoding.UTF8.GetBytes(logs);
            using (MemoryStream memoryStream = new MemoryStream(byteArray))
            {
                memoryStream.Position = 0; // Reset position to the beginning
                await this.assetStore.Save(memoryStream, $"{contributionId}/{this.idEncoder.Encode(disc.Id)}-logs.txt", ContentTypes.TextContentType, cancellationToken);
            }

            disc.LogsUploaded = true;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return this.Ok();

        //TODO: Notify the client a disc has been added? (to prevent the client having to poll)
    }

    [HttpPost("contribute/create")]
    [Authorize]
    public async Task<CreateContributionResponse> CreateContribution([FromBody] CreateContributionRequest request)
    {
        var userId = this.userManager.GetUserId(User);
        //var user = await this.userManager.FindByIdAsync(userId!);

        var contribution = new UserContribution
        {
            UserId = userId,
            Created = DateTimeOffset.UtcNow,
            Asin = request.Asin,
            ExternalId = request.ExternalId,
            ExternalProvider = request.ExternalProvider,
            MediaType = request.MediaType,
            ReleaseDate = request.ReleaseDate,
            Status = "New",
            FrontImageUrl = request.FrontImageUrl,
            BackImageUrl = request.BackImageUrl,
            Upc = request.Upc,
            ReleaseTitle = request.ReleaseTitle,
            ReleaseSlug = request.ReleaseSlug
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync();
        {
            dbContext.UserContributions.Add(contribution);
            await dbContext.SaveChangesAsync();
        }

        // Save images to blob storage? (or just store the url and do that later)

        return new CreateContributionResponse { ContributionId = this.idEncoder.Encode(contribution.Id) };
    }

    [HttpPost("contribute/saveDisc")]
    [Authorize]
    public async Task<SaveDiscResponse> CreateContribution([FromBody] SaveDiscRequest request)
    {
        var disc = new UserContributionDisc
        {
            ContentHash = request.ContentHash,
            Index = request.Index,
            Format = request.Format,
            Name = request.Name,
            Slug = request.Slug
        };

        await using var dbContext = await this.dbContextFactory.CreateDbContextAsync();
        {
            var contributionId = this.idEncoder.Decode(request.ContributionId).Single();
            var discId = this.idEncoder.Decode(request.ContributionId).Single();
            var contribution = await dbContext.UserContributions
                .Include(c => c.Discs)
                .FirstOrDefaultAsync(c => c.Id == contributionId);

            if (contribution == null)
            {
                throw new Exception("Contribution not found");
            }

            var existingDisc = contribution?.Discs.FirstOrDefault(d => d.ContentHash == disc.ContentHash);
            if (existingDisc != null)
            {
                existingDisc.Index = request.Index;
                existingDisc.Format = request.Format;
                existingDisc.Name = request.Name;
                existingDisc.Slug = request.Slug;
            }
            else
            {
                contribution!.Discs.Add(disc);
            }

            await dbContext.SaveChangesAsync();
        }

        return new SaveDiscResponse { DiscId = this.idEncoder.Encode(disc.Id) };
    }
}