using Microsoft.AspNetCore.Components;

namespace TheDiscDb.Client;

public abstract class CancellableComponentBase : ComponentBase, IDisposable
{
    private CancellationTokenSource? cancellationTokenSource;

    protected CancellationToken CancellationToken => (cancellationTokenSource ??= new()).Token;

    public virtual void Dispose()
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }
}
