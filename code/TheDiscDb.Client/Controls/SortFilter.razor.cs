using Microsoft.AspNetCore.Components;
using Syncfusion.Blazor.Buttons;

namespace TheDiscDb.Client.Controls;

public record SortItemDefinition<T>
{
    public SortEnumType Default { get; set; }
    public T? Ascending { get; set; }
    public T? Descending { get; set; }
    public string? DisplayText { get; set; }
    public string? Id { get; set; }
    public bool IsDefault { get; set; }

    public IReadOnlyList<T> GetOrderBy(SortEnumType direction)
    {
        var item = direction == SortEnumType.Asc ? this.Ascending : this.Descending;

        if (item == null)
        {
            return [];
        }

        return new List<T> { item };
    }
}

public partial class SortFilter<TSortItem> : ComponentBase
{
    public IconName SortIcon { get; set; } = IconName.SortAscending;
    public SortEnumType SortDirection { get; set; } = SortEnumType.Asc;
    public SortItemDefinition<TSortItem>? SelectedSortDefintion { get; set; }
    public string SortDirectionDescription => BuildSortDirectionString();

    private string BuildSortDirectionString()
    {
        string direction = this.SortDirection == SortEnumType.Asc ? "Ascending" : "Descending";
        string oppositeDirection = this.SortDirection == SortEnumType.Asc ? "Descending" : "Ascending";
        string displayText = this.SelectedSortDefintion?.DisplayText ?? "unknown";
        return $"{displayText} - {direction} (Click to change to {oppositeDirection})";
    }

    [Parameter]
    public IEnumerable<SortItemDefinition<TSortItem>>? AllItems { get; set; }

    [Parameter]
    public EventCallback<(SortItemDefinition<TSortItem> Definition, SortEnumType Direction)> SelectionChanged { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (this.AllItems != null)
        {
            this.SelectedSortDefintion = this.AllItems.FirstOrDefault(x => x.IsDefault);
        }

        this.SortDirection = this.SelectedSortDefintion?.Default ?? SortEnumType.Asc;
        this.SortIcon = this.SortDirection == SortEnumType.Asc ? IconName.SortAscending : IconName.SortDescending;
    }

    private async void SortDirectionClicked(Microsoft.AspNetCore.Components.Web.MouseEventArgs e)
    {
        await TrySortBy(this.SelectedSortDefintion?.Id);
    }
    private async Task SortDefinitionChanged(ChangeEventArgs e)
    {
        if (e?.Value == null)
        {
            return;
        }

        var id = e.Value.ToString();
        await TrySortBy(id);
    }

    private async Task TrySortBy(string? id)
    {
        if (this.AllItems == null || string.IsNullOrEmpty(id))
        {
            return;
        }

        var item = this.AllItems.FirstOrDefault(x => x.Id == id);
        if (item != null && this.SelectedSortDefintion != item)
        {
            this.SortDirection = item.Default;
            this.SortIcon = this.SortDirection == SortEnumType.Asc ? IconName.SortAscending : IconName.SortDescending;
        }
        else
        {
            this.SortDirection = this.SortDirection == SortEnumType.Asc ? SortEnumType.Desc : SortEnumType.Asc;
            this.SortIcon = this.SortDirection == SortEnumType.Asc ? IconName.SortAscending : IconName.SortDescending;
        }

        this.SelectedSortDefintion = item;
        var selection = (item!, this.SortDirection);
        await this.SelectionChanged.InvokeAsync(selection);
    }
}
