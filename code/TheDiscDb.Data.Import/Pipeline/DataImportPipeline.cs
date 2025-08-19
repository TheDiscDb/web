using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TheDiscDb.Data.Import.Pipeline;

public class DataImportPipeline : IAsyncDisposable
{
    private readonly IList<IMiddleware> middlewares;

    internal DataImportPipeline(IEnumerable<IMiddleware> middlewares)
    {
        this.middlewares = middlewares.ToList();

        Func<ImportItem, CancellationToken, Task> mediaItemNext = (_, _) => Task.CompletedTask;
        for (int i = this.middlewares.Count - 1; i >= 0; i--)
        {
            var current = this.middlewares[i];
            current.Next = mediaItemNext;

            mediaItemNext = current.Process;
        }
    }

    public async Task ProcessItem(ImportItem item, CancellationToken cancellationToken = default)
    {
        var first = this.middlewares.FirstOrDefault();
        if (first != null)
        {
            await first.Process(item, cancellationToken).ConfigureAwait(false);
        }
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
