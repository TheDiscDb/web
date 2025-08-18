namespace TheDiscDb.Data.GraphQL;

using System.Linq;
using HotChocolate;
using HotChocolate.Data;
using HotChocolate.Types;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

public class Query
{
    const int MaxPageSize = 100;
    const int DefaultPageSize = 50;

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<MediaItem> GetMediaItems(SqlServerDataContext context) => context.MediaItems;


    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<Boxset> GetBoxsets(SqlServerDataContext context) => context.BoxSets;

    [UsePaging(MaxPageSize = MaxPageSize, DefaultPageSize = DefaultPageSize)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public IQueryable<MediaItem> GetMediaItemsByGroup(SqlServerDataContext context, string slug, string? role = null)
    {
        if (string.IsNullOrEmpty(role))
        {
            return context.MediaItems.Where(i => i.MediaItemGroups.Any(g => g != null && g.Group != null && g.Group.Slug == slug));
        }

        return context.MediaItems.Where(i => i.MediaItemGroups.Any(g => g != null && g.Group != null && g.Role == role && g.Group.Slug == slug));
    }
}
