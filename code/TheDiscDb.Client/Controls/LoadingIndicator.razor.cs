using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client.Controls;

public enum LoadingIndicatorSize
{
    Small,
    Medium,
    Large,
}

public partial class LoadingIndicator : ComponentBase
{
    /// <summary>
    /// When <c>false</c> the indicator renders nothing, so callers can bind it directly to a busy flag.
    /// </summary>
    [Parameter]
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Optional text shown next to (or below) the spinner. When omitted a screen-reader-only
    /// "Loading…" label is rendered instead.
    /// </summary>
    [Parameter]
    public string? Message { get; set; }

    [Parameter]
    public LoadingIndicatorSize Size { get; set; } = LoadingIndicatorSize.Medium;

    /// <summary>
    /// Renders the indicator as an overlay that fills its nearest positioned ancestor.
    /// The parent must set <c>position: relative</c> for the overlay to be contained.
    /// </summary>
    [Parameter]
    public bool Overlay { get; set; }

    /// <summary>
    /// Stacks the message beneath the spinner instead of placing it alongside.
    /// </summary>
    [Parameter]
    public bool Vertical { get; set; }

    /// <summary>
    /// Extra CSS classes appended to the root element.
    /// </summary>
    [Parameter]
    public string? CssClass { get; set; }

    private string SpinnerSize => this.Size switch
    {
        LoadingIndicatorSize.Small => "18",
        LoadingIndicatorSize.Large => "44",
        _ => "28",
    };

    private string RootCssClass
    {
        get
        {
            var classes = new List<string>(5) { "loading-indicator" };

            classes.Add(this.Size switch
            {
                LoadingIndicatorSize.Small => "loading-indicator--small",
                LoadingIndicatorSize.Large => "loading-indicator--large",
                _ => "loading-indicator--medium",
            });

            if (this.Overlay)
            {
                classes.Add("loading-indicator--overlay");
            }

            if (this.Vertical || this.Overlay)
            {
                classes.Add("loading-indicator--vertical");
            }

            if (!string.IsNullOrWhiteSpace(this.CssClass))
            {
                classes.Add(this.CssClass);
            }

            return string.Join(' ', classes);
        }
    }
}
