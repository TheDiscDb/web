using TheDiscDb.Data.Import.Pipeline;
using TheDiscDb.InputModels;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ImportPipelineExtensions
    {
        public static IServiceCollection AddImportPipeline(this IServiceCollection services)
        {
            services.AddSingleton<IServiceCollection>(services);
            services.AddSingleton<DataImportPipelineBuilder>();
            services.AddSingleton<DataImportItemFactory>();
            services.AddSingleton<DatabaseImportMiddleware>();
            services.AddSingleton<ExceptionHandlingMiddleware>();
            services.AddSingleton<GroupImportMiddleware>();
            services.AddSingleton<CoverImageUploadMiddleware>();
            services.AddSingleton<LatestReleaseUpdateMiddleware>();
            services.AddSingleton<SearchIndexUpdateMiddleware>();

            services.AddSingleton<IItemHandler<MediaItem>, MediaItemHandler>();
            services.AddSingleton<IItemHandler<Boxset>, BoxsetItemHandler>();
            services.AddSingleton<IItemHandler<Release>, ReleaseItemHandler>();
            services.AddSingleton<IItemHandler<Disc>, DiscItemHandler>();
            services.AddSingleton<IItemHandler<DiscItemReference>, DiscItemReferenceItemHandler>();
            services.AddSingleton<IItemHandler<Title>, TitleItemHandler>();
            services.AddSingleton<IItemHandler<Track>, TrackItemHandler>();
            return services;
        }
    }
}
