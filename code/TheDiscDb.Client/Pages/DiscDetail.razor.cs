using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.QuickGrid;
using StrawberryShake;
using TheDiscDb.Client.Contributions;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages;

public partial class DiscDetail : ComponentBase
{
    private sealed record CopyButtonState(string IconClassName, bool IsDisabled = false);

    private static readonly CopyButtonState DefaultCopyState = new("e-icons e-copy");
    private static readonly CopyButtonState CopiedCopyState = new("e-icons e-circle-check", IsDisabled: true);

    private readonly Dictionary<IDiscItem, CopyButtonState> descriptionCopyStates = new();
    private readonly Dictionary<IDiscItem, CopyButtonState> filenameCopyStates = new();
    private readonly Dictionary<string, CopyButtonState> discIdCopyStates = new();
    private readonly Dictionary<IDiscItem, string> filenamesByItem = new();

    private IReadOnlyList<FileNameTemplateInput>? userTemplates;

    [Inject]
    public GetDiscDetailQuery? MediaItemQuery { get; set; }

    [Inject]
    public GetBoxsetDiscsQuery? BoxsetQuery { get; set; }

    [Inject]
    public GetDiscDetailByContentHashQuery? ContentHashQuery { get; set; }

    [Inject]
    private IClipboardService Clipboard { get; set; } = null!;

    [Inject]
    public GetMyFileNameTemplatesQuery? MyTemplatesQuery { get; set; }

    [Inject]
    private AuthenticationStateProvider? AuthProvider { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Parameter]
    public string? SlugOrIndexString { get; set; }

    [Parameter]
    public string? ContentHash { get; set; }

    private int Index => SlugOrIndex.Index ?? 0;

    private SlugOrIndex SlugOrIndex => SlugOrIndex.Create(SlugOrIndexString);

    private IDisplayItem? Item { get; set; }
    private IDisplayItem? Release { get; set; }
    private IDisc? Disc { get; set; }
    private string? DiscGlobalDiscId { get; set; }
    private string? DiscContentHash { get; set; }

    private IEnumerable<IDiscItem> AllTitles { get; set; } = new List<IDiscItem>();
    private IQueryable<IDiscItem> FilteredTitles { get; set; } = new List<IDiscItem>().AsQueryable();
    private readonly GridSort<IDiscItem> SortSourceFile = GridSort<IDiscItem>.ByAscending(u => u.SourceFile);
    private readonly GridSort<IDiscItem> SortDescription = GridSort<IDiscItem>.ByAscending(u => u.Description);
    private readonly GridSort<IDiscItem> SortSegmentMap = GridSort<IDiscItem>.ByAscending(u => u.SegmentMap);
    private GridSort<IDiscItem> SortFilename => GridSort<IDiscItem>.ByAscending(t => GetFileName(t));

    private CopyButtonState GetDescriptionCopyState(IDiscItem item)
    {
        return descriptionCopyStates.TryGetValue(item, out var state) ? state : DefaultCopyState;
    }

    private CopyButtonState GetFileNameCopyState(IDiscItem item)
    {
        return filenameCopyStates.TryGetValue(item, out var state) ? state : DefaultCopyState;
    }

    private CopyButtonState GetDiscIdCopyState(string key)
    {
        return discIdCopyStates.TryGetValue(key, out var state) ? state : DefaultCopyState;
    }

    private async Task CopyDiscIdToClipboard(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        discIdCopyStates[key] = CopiedCopyState;
        StateHasChanged();

        try
        {
            await Clipboard.WriteTextAsync(value);
        }
        catch
        {
            discIdCopyStates.Remove(key);
            StateHasChanged();
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        discIdCopyStates.Remove(key);
        StateHasChanged();
    }

    private string GetFileName(IDiscItem item)
    {
        return filenamesByItem.TryGetValue(item, out var name) ? name : string.Empty;
    }

    private async Task CopyDescriptionToClipboard(IDiscItem item)
    {
        if (string.IsNullOrEmpty(item.Description))
        {
            return;
        }

        descriptionCopyStates[item] = CopiedCopyState;
        StateHasChanged();

        try
        {
            await Clipboard.WriteTextAsync(item.Description);
        }
        catch
        {
            descriptionCopyStates.Remove(item);
            StateHasChanged();
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        descriptionCopyStates.Remove(item);
        StateHasChanged();
    }

    private async Task CopyFileNameToClipboard(IDiscItem item)
    {
        var filename = GetFileName(item);
        if (string.IsNullOrEmpty(filename))
        {
            return;
        }

        filenameCopyStates[item] = CopiedCopyState;
        StateHasChanged();

        try
        {
            await Clipboard.WriteTextAsync(filename);
        }
        catch
        {
            filenameCopyStates.Remove(item);
            StateHasChanged();
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        filenameCopyStates.Remove(item);
        StateHasChanged();
    }

    protected override async Task OnInitializedAsync()
    {
        // Load the user's persisted overrides (if any) so the disc-detail
        // queries below send them as the templates variable. Anonymous users
        // and request failures simply send no templates and the server uses
        // its built-in defaults.
        await LoadUserTemplatesAsync();

        if (!string.IsNullOrEmpty(this.ContentHash))
        {
            await HandleContentHashLookup();
        }
        else if (this.Type!.Equals("Boxset", StringComparison.OrdinalIgnoreCase))
        {
            await HandleBoxset();
        }
        else
        {
            await HandleMovieOrSeries();
        }
    }

    private async Task LoadUserTemplatesAsync()
    {
        if (this.AuthProvider is null || this.MyTemplatesQuery is null)
        {
            return;
        }

        try
        {
            var state = await this.AuthProvider.GetAuthenticationStateAsync();
            if (state.User?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var result = await this.MyTemplatesQuery.ExecuteAsync();
            if (!result.IsSuccessResult() || result.Data?.MyFileNameTemplates is null)
            {
                return;
            }

            var list = new List<FileNameTemplateInput>();
            foreach (var t in result.Data.MyFileNameTemplates)
            {
                if (string.IsNullOrWhiteSpace(t.ItemType) || string.IsNullOrWhiteSpace(t.Template))
                {
                    continue;
                }

                list.Add(new FileNameTemplateInput
                {
                    ItemType = t.ItemType,
                    Template = t.Template,
                });
            }

            if (list.Count > 0)
            {
                this.userTemplates = list;
            }
        }
        catch
        {
            // Anonymous calls or transient failures fall through with no overrides.
        }
    }

    private async Task HandleContentHashLookup()
    {
        var result = await this.ContentHashQuery!.ExecuteAsync(this.ContentHash, this.userTemplates);
        if (!result.IsSuccessResult())
        {
            return;
        }

        var item = result.Data!.MediaItems!.Nodes!.FirstOrDefault();
        this.Item = item;
        if (Item == null)
        {
            return;
        }

        var release = item!.Releases!.First();
        this.Release = release;

        if (Release == null)
        {
            return;
        }

        var disc = release!.Discs!.First();
        this.Disc = disc;
        this.DiscGlobalDiscId = disc?.GlobalDiscId;
        this.DiscContentHash = disc?.ContentHash;

        if (Disc == null)
        {
            return;
        }

        AllTitles = disc!.Titles!;
        FilteredTitles = AllTitles.Where(t => t.HasItem).AsQueryable();

        foreach (var t in disc.Titles!)
        {
            if (!string.IsNullOrEmpty(t.Filename))
            {
                this.filenamesByItem[t] = t.Filename;
            }
        }
    }

    private async Task HandleMovieOrSeries()
    {
        var result = await this.MediaItemQuery!.ExecuteAsync(this.Slug, this.ReleaseSlug, this.Index, SlugOrIndex.Slug, this.Type, this.userTemplates);
        if (!result.IsSuccessResult())
        {
            return;
        }

        var item = result.Data!.MediaItems!.Nodes!.FirstOrDefault();
        this.Item = item;
        if (Item == null)
        {
            return;
        }

        var release = item!.Releases!.First();
        this.Release = release;

        if (Release == null)
        {
            return;
        }

        var disc = release!.Discs!.FirstOrDefault(d => SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
        this.Disc = disc;
        this.DiscGlobalDiscId = disc?.GlobalDiscId;
        this.DiscContentHash = disc?.ContentHash;

        if (Disc == null)
        {
            return;
        }

        AllTitles = disc!.Titles!;
        FilteredTitles = AllTitles.Where(t => t.HasItem).AsQueryable();

        foreach (var t in disc.Titles!)
        {
            if (!string.IsNullOrEmpty(t.Filename))
            {
                this.filenamesByItem[t] = t.Filename;
            }
        }
    }

    private async Task HandleBoxset()
    {
        var result = await this.BoxsetQuery!.ExecuteAsync(this.Slug, this.Index, SlugOrIndex.Slug, this.userTemplates);
        if (!result.IsSuccessResult())
        {
            return;
        }

        var item = result.Data!.Boxsets!.Nodes!.FirstOrDefault();
        this.Item = item;
        if (Item == null)
        {
            return;
        }

        var release = item!.Release;
        this.Release = release;

        if (Release == null)
        {
            return;
        }

        var disc = release!.Discs!.FirstOrDefault(d => SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
        this.Disc = disc;
        this.DiscGlobalDiscId = disc?.GlobalDiscId;
        this.DiscContentHash = disc?.ContentHash;

        if (Disc == null)
        {
            return;
        }

        AllTitles = disc!.Titles!;
        FilteredTitles = AllTitles.Where(t => t.HasItem).AsQueryable();

        foreach (var t in disc.Titles!)
        {
            if (!string.IsNullOrEmpty(t.Filename))
            {
                this.filenamesByItem[t] = t.Filename;
            }
        }
    }

    private Task AllTitlesChanged(Syncfusion.Blazor.Buttons.ChangeEventArgs<bool> args)
    {
        if (args.Checked)
        {
            FilteredTitles = AllTitles.AsQueryable();
        }
        else
        {
            FilteredTitles = AllTitles.Where(t => t.HasItem).AsQueryable();
        }

        this.StateHasChanged();
        return Task.CompletedTask;
    }

    public string TitleDetailUrl(IDiscItem title)
    {
        if (title == null)
        {
            return string.Empty;
        }

        return $"/{Item!.Type!.ToLower()}/{Slug}/releases/{ReleaseSlug}/discs/{SlugOrIndex.UrlValue}/{NavigationExtensions.GetFile(title.SourceFile!)}/{NavigationExtensions.GetExtension(title.SourceFile!)}";
    }

    // Builds the "Help add it" link to the backfill page, carrying this disc's identity (so the
    // server can warn on a wrong disc) and a return URL back to this page.
    private string GetAddDiscIdUrl()
    {
        var isBoxset = string.Equals(Item?.Type, "Boxset", StringComparison.OrdinalIgnoreCase);
        var query = new List<string>
        {
            $"{(isBoxset ? "boxset" : "media")}={Uri.EscapeDataString(Slug ?? string.Empty)}",
            $"release={Uri.EscapeDataString(ReleaseSlug ?? string.Empty)}",
        };

        if (!string.IsNullOrEmpty(Disc?.Slug))
        {
            query.Add($"disc={Uri.EscapeDataString(Disc.Slug)}");
        }
        else if (Disc is not null)
        {
            query.Add($"index={Disc.Index}");
        }

        var returnUrl = new Uri(Navigation.Uri).PathAndQuery;
        query.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");

        return "/contribute/discid?" + string.Join("&", query);
    }

    private string? lastSortColumn = null;
    private string lastSortDirection = "asc";

    public void SortTable(string column)
    {
        string direction = lastSortDirection;
        if (lastSortColumn == column)
        {
            if (direction == "asc")
            {
                direction = "desc";
            }
            else
            {
                direction = "asc";
            }
        }
        else
        {
            lastSortColumn = column;
        }

        lastSortDirection = direction;
        FilteredTitles = Helper.OrderByDynamic(FilteredTitles, column, direction).AsQueryable();
    }
}

public class Helper
{
    public static IEnumerable<T> OrderByDynamic<T>(IEnumerable<T> items, string sortby, string sort_direction)
    {
        var property = typeof(T).GetProperty(sortby);

        if (property == null)
        {
            throw new Exception($"A property named {sortby} not found on {typeof(T).FullName}");
        }

        var result = typeof(Helper)
            .GetMethod("OrderByDynamic_Private", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), property.PropertyType)
            .Invoke(null, new object[] { items, sortby, sort_direction });

        var enumerable = result as IEnumerable<T>;

        if (enumerable == null)
        {
            return Enumerable.Empty<T>();
        }

        return enumerable;
    }

    private static IEnumerable<T> OrderByDynamic_Private<T, TKey>(IEnumerable<T> items, string sortby, string sort_direction)
    {
        var parameter = Expression.Parameter(typeof(T), "x");

        Expression<Func<T, TKey>> property_access_expression =
            Expression.Lambda<Func<T, TKey>>(
                Expression.Property(parameter, sortby),
                parameter);

        if (sort_direction == "asc")
        {
            return items.OrderBy(property_access_expression.Compile());
        }

        if (sort_direction == "desc")
        {
            return items.OrderByDescending(property_access_expression.Compile());
        }

        throw new Exception("Invalid Sort Direction");
    }
}
