namespace TheDiscDb.Web.Controllers
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using SixLabors.ImageSharp.Formats.Png;
    using SixLabors.ImageSharp.Processing;
    using TheDiscDb.Search;
    using TheDiscDb.Web.Barcode;

    [ApiController]
    [Route("api")]
    public class ClientApiController : ControllerBase
    {
        private readonly ISearchService search;

        public ClientApiController(ISearchService search)
        {
            this.search = search ?? throw new System.ArgumentNullException(nameof(search));
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
            var barcode = new Barcode(data)
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
    }
}
