namespace TheDiscDb.Components.Controls;

using Microsoft.AspNetCore.Components;
using TheDiscDb.Services.Achievements;

public partial class BadgeIcon : ComponentBase
{
    [Inject]
    private BadgeIconStore IconStore { get; set; } = null!;

    /// <summary>Icon asset key (SVG file name without extension) under wwwroot/badges/.</summary>
    [Parameter]
    public string? Icon { get; set; }

    [Parameter]
    public AchievementTier Tier { get; set; } = AchievementTier.None;

    /// <summary>Tooltip text (usually the achievement name / description).</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>When false the badge renders in a muted "locked" state.</summary>
    [Parameter]
    public bool Earned { get; set; } = true;

    private MarkupString Svg => IconStore.GetSvg(Icon);

    private string TierClass => Tier.ToString().ToLowerInvariant();

    private string AriaLabel => string.IsNullOrWhiteSpace(Title) ? "Achievement badge" : Title!;
}
