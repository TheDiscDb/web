namespace TheDiscDb.Services.Contributions;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration for the shared contribution services. The consumer must additionally register a
/// <c>SqlServerDataContext</c>, a <c>SqidsEncoder&lt;int&gt;</c>, and an <c>IStaticAssetStore</c> bound
/// to the <b>contributions</b> blob container.
/// </summary>
public static class ContributionsServiceCollectionExtensions
{
    public static IServiceCollection AddContributionDiscServices(this IServiceCollection services)
    {
        services.AddScoped<IContributionDiscService, ContributionDiscService>();
        return services;
    }
}
