using System;
using System.Threading;
using System.Threading.Tasks;

namespace TheDiscDb.Data.Import.Pipeline;

public interface IMiddleware
{
    Task Process(ImportItem item, CancellationToken cancellationToken);
    Func<ImportItem, CancellationToken, Task> Next { get; set; }
}
