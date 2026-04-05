using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using TheDiscDb.Client;
using TheDiscDb.Core.DiscHash;
using TheDiscDb.Search;

namespace TheDiscDb.Web.Controllers;

[ApiController]
[Route("api")]
public class ClientApiController : ControllerBase
{
    private readonly ISearchService search;
    private readonly ISearchIndexService searchIndex;

    public ClientApiController(ISearchService search, ISearchIndexService searchIndex)
    {
        this.search = search ?? throw new System.ArgumentNullException(nameof(search));
        this.searchIndex = searchIndex ?? throw new System.ArgumentNullException(nameof(searchIndex));
    }

    [HttpGet("search")]
    public async Task<IEnumerable<SearchEntry>> Search(string q, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return [];

        if (limit.HasValue)
            limit = Math.Min(limit.Value, 10);

        return await this.search.Search(q, limit, cancellationToken);
    }

    [HttpGet("barcode")]
    public FileContentResult Barcode(string data, int width = 200, bool showLabel = true)
    {
        var barcode = new Barcode.Barcode(data)
        {
            ShowLabel = showLabel
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

    [Authorize("Admin")]
    [HttpGet("search/rebuild-index")]
    public async Task<BuildIndexSummary> RebuildIndex()
    {
        return await this.searchIndex.BuildIndex();
    }

    [HttpGet("search/external")]
    public Task<IEnumerable<ExternalSearchEntry>> ExternalSearch(string s)
    {
        throw new NotImplementedException();
    }
}