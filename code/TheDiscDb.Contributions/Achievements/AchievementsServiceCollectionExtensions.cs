namespace TheDiscDb.Services.Achievements;

using Microsoft.Extensions.DependencyInjection;

/// <summary>DI registration for the badges &amp; achievements feature.</summary>
public static class AchievementsServiceCollectionExtensions
{
    public static IServiceCollection AddAchievements(this IServiceCollection services)
    {
        services.AddScoped<IContributorStatsBuilder, ContributorStatsBuilder>();
        services.AddScoped<IAchievementService, AchievementService>();
        return services;
    }
}
