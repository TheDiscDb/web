using TheDiscDb.Data.Import.Pipeline;

namespace TheDiscDb.Services.Admin;

public class ContributionImportPipelineRunner
{
    private readonly DataImportPipelineBuilder pipelineBuilder;
    private readonly DataImportItemFactory itemFactory;
    private readonly ExceptionHandlingMiddleware exceptionMiddleware;
    private readonly DatabaseImportMiddleware databaseMiddleware;
    private readonly GroupImportMiddleware groupImportMiddleware;
    private readonly CoverImageUploadMiddleware coverImageUploadMiddleware;
    private readonly LatestReleaseUpdateMiddleware latestReleaseMiddleware;

    public ContributionImportPipelineRunner(
        DataImportPipelineBuilder pipelineBuilder,
        DataImportItemFactory itemFactory,
        ExceptionHandlingMiddleware exceptionMiddleware,
        DatabaseImportMiddleware databaseMiddleware,
        GroupImportMiddleware groupImportMiddleware,
        CoverImageUploadMiddleware coverImageUploadMiddleware,
        LatestReleaseUpdateMiddleware latestReleaseMiddleware)
    {
        this.pipelineBuilder = pipelineBuilder;
        this.itemFactory = itemFactory;
        this.exceptionMiddleware = exceptionMiddleware;
        this.databaseMiddleware = databaseMiddleware;
        this.groupImportMiddleware = groupImportMiddleware;
        this.coverImageUploadMiddleware = coverImageUploadMiddleware;
        this.latestReleaseMiddleware = latestReleaseMiddleware;
    }

    public async Task RunAsync(string inputDirectory, Action<string> log, CancellationToken cancellationToken = default)
    {
        log("Finding items to import...");
        var mediaItems = await this.itemFactory.FindMediaItemsToProcess(inputDirectory);
        log($"Found {mediaItems.Count()} item(s) to import.");

        var pipeline = this.pipelineBuilder
            .Use(this.exceptionMiddleware)
            .Use(this.latestReleaseMiddleware)
            .Use(this.coverImageUploadMiddleware)
            .Use(this.databaseMiddleware)
            .Use(this.groupImportMiddleware)
            .Build();

        int count = 0;
        foreach (var mediaItem in mediaItems)
        {
            string itemLabel = mediaItem.MediaItem?.FullTitle ?? mediaItem.Boxset?.Title ?? "unknown";
            log($"Importing: {itemLabel}");
            await pipeline.ProcessItem(mediaItem, cancellationToken);
            count++;
            log($"Imported ({count}): {itemLabel}");
        }

        log($"Import complete. {count} item(s) processed.");
    }
}
