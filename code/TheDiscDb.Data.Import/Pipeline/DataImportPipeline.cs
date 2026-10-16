using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheDiscDb.Data.Import.Pipeline;

public class DataImportPipeline : IAsyncDisposable
{
    private readonly IList<IMiddleware> middlewares;
    private readonly Func<ImportItem, CancellationToken, Task> process;

    internal DataImportPipeline(IEnumerable<IMiddleware> middlewares)
    {
        this.middlewares = middlewares.ToList();

        Func<ImportItem, CancellationToken, Task> mediaItemNext = (_, _) => Task.CompletedTask;
        for (int i = this.middlewares.Count - 1; i >= 0; i--)
        {
            var current = this.middlewares[i];
            current.Next = mediaItemNext;

            mediaItemNext = async (item, cancellationToken) =>
            {
                var diagnostics = item.Diagnostics;
                if (diagnostics == null)
                {
                    await current.Process(item, cancellationToken);
                    return;
                }

                var previouslyMeasured = diagnostics.MeasuredDuration;
                var watch = Stopwatch.StartNew();
                bool succeeded = false;
                try
                {
                    await current.Process(item, cancellationToken);
                    succeeded = !diagnostics.FailedStages.Contains(current.GetType().Name);
                }
                finally
                {
                    watch.Stop();
                    var downstream = diagnostics.MeasuredDuration - previouslyMeasured;
                    diagnostics.Stages.Add(new ImportStageTiming(
                        current.GetType().Name,
                        watch.Elapsed - downstream,
                        succeeded));
                    diagnostics.Failed |= !succeeded;
                }
            };
        }

        this.process = mediaItemNext;
    }

    public async Task ProcessItem(ImportItem item, CancellationToken cancellationToken = default)
    {
        await this.process(item, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var item in this.middlewares)
        {
            (item as IDisposable)?.Dispose();
            if (item is IAsyncDisposable d)
            {
                await d.DisposeAsync();
            }
        }
    }
}
