using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Client;
using TheDiscDb.Search;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web.Controllers;

[ApiController]
[Route("api")]
public class ClientApiController : ControllerBase
{
    private readonly ISearchService search;
    private readonly IDbContextFactory<SqlServerDataContext> dbContextFactory;

    public ClientApiController(ISearchService search, IDbContextFactory<SqlServerDataContext> dbContextFactory)
    {
        this.search = search ?? throw new System.ArgumentNullException(nameof(search));
        this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
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

    [HttpPost("contribute/{contributionId}/addDisc")]
    public void AddDisc(string contributionId, [FromBody] string logs)
    {
        //1. Retrieve the contribution from the database
        //2. Save the logs in blob storage
        //3. Add the disc to the contribution and save to the database
        //4. Notify the client a disc has been added? (to prevent the client having to poll)
        Console.WriteLine(logs);
    }
}