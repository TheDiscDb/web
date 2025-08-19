using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Components.Controls
{
    public partial class AmazonAssociatesButton : ComponentBase
    {
        [Parameter]
        public string? Asin { get; set; }

        [Parameter]
        public int Width { get; set; } = 17;

        private string GetUrl()
        {
            return $"https://www.amazon.com/dp/{Asin}/ref=nosim?tag=thediscdb07-20";
        }
    }
}
