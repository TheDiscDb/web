namespace TheDiscDb.Data.Import.Pipeline;

using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

public class ExceptionHandlingMiddleware : IMiddleware
{
    public Func<ImportItem, CancellationToken, Task> Next { get; set; } = (_, _) => Task.CompletedTask;

    public async Task Process(ImportItem item, CancellationToken cancellationToken)
    {
        try
        {
            await this.Next(item, cancellationToken);
        }
        catch (Exception e)
        {
            AnsiConsole.WriteException(e);
        }
    }
}
