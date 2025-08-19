using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Data.GraphQL;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client.Pages;

public partial class DiscDetail : ComponentBase
{
    [Inject]
    public GetDiscDetailQuery? MediaItemQuery { get; set; }

    [Inject]
    public GetBoxsetDiscsQuery? BoxsetQuery { get; set; }

    [Inject]
    public GetDiscDetailByContentHashQuery? ContentHashQuery { get; set; }

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

    private IEnumerable<IDiscItem> AllTitles { get; set; } = new List<IDiscItem>();
    private IEnumerable<IDiscItem> FilteredTitles { get; set; } = new List<IDiscItem>();

    protected override async Task OnInitializedAsync()
    {
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

    private async Task HandleContentHashLookup()
    {
        var result = await this.ContentHashQuery!.ExecuteAsync(this.ContentHash);
        if (result.IsSuccessResult())
        {
            var item = result!.Data!.MediaItems!.Nodes!.FirstOrDefault();
            this.Item = item;
            if (Item != null)
            {
                var release = item!.Releases!.First();
                this.Release = release;

                if (Release != null)
                {
                    var disc = release!.Discs!.First();
                    this.Disc = disc;

                    if (Disc != null)
                    {
                        AllTitles = disc!.Titles!;
                        FilteredTitles = AllTitles.Where(t => t.HasItem);
                    }
                }
            }
        }

    }

    private async Task HandleMovieOrSeries()
    {
        var result = await this.MediaItemQuery!.ExecuteAsync(this.Slug, this.ReleaseSlug, this.Index, SlugOrIndex.Slug, this.Type);
        if (result.IsSuccessResult())
        {
            var item = result!.Data!.MediaItems!.Nodes!.FirstOrDefault();
            this.Item = item;
            if (Item != null)
            {
                var release = item!.Releases!.First();
                this.Release = release;

                if (Release != null)
                {
                    var disc = release!.Discs!.FirstOrDefault(d => SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
                    this.Disc = disc;

                    if (Disc != null)
                    {
                        AllTitles = disc!.Titles!;
                        FilteredTitles = AllTitles.Where(t => t.HasItem);
                    }
                }
            }
        }
    }

    private async Task HandleBoxset()
    {
        var result = await this.BoxsetQuery!.ExecuteAsync(this.Slug, this.Index, SlugOrIndex.Slug);
        if (result.IsSuccessResult())
        {
            var item = result!.Data!.Boxsets!.Nodes!.FirstOrDefault();
            this.Item = item;
            if (Item != null)
            {
                var release = item!.Release;
                this.Release = release;

                if (Release != null)
                {
                    var disc = release!.Discs!.FirstOrDefault(d => SlugOrIndex.Create(d.Slug, d.Index) == SlugOrIndex.Create(SlugOrIndexString));
                    this.Disc = disc;

                    if (Disc != null)
                    {
                        AllTitles = disc!.Titles!;
                        FilteredTitles = AllTitles.Where(t => t.HasItem);
                    }
                }
            }
        }
    }

    private Task AllTitlesChanged(bool state)
    {
        if (state)
        {
            FilteredTitles = AllTitles;
        }
        else
        {
            FilteredTitles = AllTitles.Where(t => t.HasItem);
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
        FilteredTitles = Helper.OrderByDynamic(FilteredTitles, column, direction).ToList();
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
