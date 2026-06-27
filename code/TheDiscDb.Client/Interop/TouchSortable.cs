using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TheDiscDb.Client.Interop;

/// <summary>
/// Shared touch/pen drag-to-reorder adapter. Bridges <c>touch-sortable.js</c> to
/// a Blazor component's existing (mouse) drag handlers so a single Pointer Events
/// implementation drives reorder on touch devices everywhere on the site.
///
/// The component supplies delegates that mirror its native HTML5 drag handlers;
/// this adapter owns the <see cref="DotNetObjectReference{T}"/> and exposes the
/// JSInvokable surface (<see cref="StartDrag"/>/<see cref="MoveRow"/>/<see
/// cref="EndDrag"/>) that the JS module calls. Index-based callbacks are resolved
/// to items via the supplied <paramref name="items"/> accessor.
/// </summary>
/// <typeparam name="T">The row item type being reordered.</typeparam>
public sealed class TouchSortable<T> : IDisposable
{
    private readonly IJSRuntime js;
    private readonly Func<IReadOnlyList<T>> items;
    private readonly Action<T> onDragStart;
    private readonly Action<T> onDragOver;
    private readonly Func<Task> onDragEnd;
    private readonly Action stateHasChanged;
    private DotNetObjectReference<TouchSortable<T>>? selfRef;
    private bool initialized;

    /// <param name="js">The JS runtime used to call <c>touchSortable.init</c>.</param>
    /// <param name="items">Accessor for the current (rendered-order) row list, used to map indices to items.</param>
    /// <param name="onDragStart">Mirrors the component's native drag-start handler.</param>
    /// <param name="onDragOver">Mirrors the component's native drag-enter handler (performs the reorder).</param>
    /// <param name="onDragEnd">Mirrors the component's native drag-end handler (may persist asynchronously).</param>
    /// <param name="stateHasChanged">Triggers a re-render after each touch callback.</param>
    public TouchSortable(
        IJSRuntime js,
        Func<IReadOnlyList<T>> items,
        Action<T> onDragStart,
        Action<T> onDragOver,
        Func<Task> onDragEnd,
        Action stateHasChanged)
    {
        this.js = js;
        this.items = items;
        this.onDragStart = onDragStart;
        this.onDragOver = onDragOver;
        this.onDragEnd = onDragEnd;
        this.stateHasChanged = stateHasChanged;
    }

    /// <summary>
    /// Binds the touch sortable to the given container (typically the table's
    /// <c>&lt;tbody&gt;</c>). Idempotent: safe to call on every render. Call
    /// <see cref="Reset"/> first if the container element was recreated.
    /// </summary>
    public async ValueTask InitAsync(ElementReference container)
    {
        if (initialized)
        {
            return;
        }

        selfRef ??= DotNetObjectReference.Create(this);
        await js.InvokeVoidAsync("touchSortable.init", container, selfRef);
        initialized = true;
    }

    /// <summary>
    /// Marks the sortable as unbound so the next <see cref="InitAsync"/> re-binds.
    /// Use when the container element is destroyed and recreated (e.g. toggling
    /// between an edit table and a review view).
    /// </summary>
    public void Reset() => initialized = false;

    /// <summary>Touch/pen drag start, invoked from <c>touch-sortable.js</c>.</summary>
    [JSInvokable]
    public void StartDrag(int index)
    {
        var list = items();
        if (index < 0 || index >= list.Count)
        {
            return;
        }

        onDragStart(list[index]);
        stateHasChanged();
    }

    /// <summary>Touch/pen drag over a row, invoked from <c>touch-sortable.js</c>.</summary>
    [JSInvokable]
    public void MoveRow(int toIndex)
    {
        var list = items();
        if (toIndex < 0 || toIndex >= list.Count)
        {
            return;
        }

        onDragOver(list[toIndex]);
        stateHasChanged();
    }

    /// <summary>Touch/pen drag end, invoked from <c>touch-sortable.js</c>.</summary>
    [JSInvokable]
    public async Task EndDrag()
    {
        await onDragEnd();
        stateHasChanged();
    }

    public void Dispose()
    {
        selfRef?.Dispose();
        selfRef = null;
    }
}
