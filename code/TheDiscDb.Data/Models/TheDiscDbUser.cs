namespace TheDiscDb.Web.Data;

using Microsoft.AspNetCore.Identity;

public class TheDiscDbUser : IdentityUser
{
    /// <summary>
    /// Cached sum of points from earned, level-affecting achievements. Denormalised for
    /// display performance; always recomputable from <see cref="UserAchievement"/> rows via
    /// the achievement service.
    /// </summary>
    public int TotalPoints { get; set; }

    /// <summary>
    /// Cached level name derived from <see cref="TotalPoints"/>. Denormalised for display;
    /// recomputed whenever points change.
    /// </summary>
    public string? Level { get; set; }
}
