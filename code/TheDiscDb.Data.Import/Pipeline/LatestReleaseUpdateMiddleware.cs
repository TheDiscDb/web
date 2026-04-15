namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class LatestReleaseUpdateMiddleware : IMiddleware
{
    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        if (item.MediaItem != null)
        {
            var latestRelease = item.MediaItem.Releases.OrderByDescending(r => r.ReleaseDate).FirstOrDefault();
            if (latestRelease != null && latestRelease.ReleaseDate > item.MediaItem.LatestReleaseDate)
            {
                item.MediaItem.LatestReleaseDate = latestRelease.ReleaseDate;
            }
        }

        await this.Next(item, cancellationToken);
    }
}
