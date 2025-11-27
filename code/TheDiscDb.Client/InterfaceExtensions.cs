using StrawberryShake;
using TheDiscDb.InputModels;

namespace TheDiscDb.Client;

public partial interface IGetMovies_MediaItems_Nodes : IDisplayItem
{
}

public partial interface IGetBoxsets_Boxsets_Nodes : IDisplayItem
{
}

public partial interface IGetSeries_MediaItems_Nodes : IDisplayItem
{
}

public partial interface IGetDiscDetailByContentHash_MediaItems_Nodes : IDisplayItem
{
}

public partial interface IGetDiscDetailByContentHash_MediaItems_Nodes_Releases : IDisplayItem
{
}

public partial interface IGetDiscDetailByContentHash_MediaItems_Nodes_Releases_Discs : IDisc
{
}

public partial interface IGetDiscDetailByContentHash_MediaItems_Nodes_Releases_Discs_Titles : IDiscItem
{
}

public partial class GetDiscDetailByContentHash_MediaItems_Nodes_Releases_Release
{
    public string Type => "Release"; // never actually used
}

public partial class GetDiscDetailByContentHash_MediaItems_Nodes_Releases_Discs_Titles_Title
{
    public string Description => this.Item?.Title ?? string.Empty;
    public string ItemType => this.Item?.Type ?? string.Empty;
    public string Season => this.Item?.Season ?? string.Empty;
    public string Episode => this.Item?.Episode ?? string.Empty;
    public bool HasItem => this.Item != null;
}

public partial class GetBoxsets_Boxsets_Nodes_Boxset
{
    public string Type => "Boxset";
}

public partial class GetDiscDetail_MediaItems_Nodes_Releases_Release
{
    public string Type => "Release"; // never actually used
}

public partial class GetBoxsetDiscs_Boxsets_Nodes_Release_Release
{
    public string Type => "Boxset";
}

public partial class GetBoxsetDiscs_Boxsets_Nodes_Boxset
{
    public string Type => "Boxset";
    public string ImageUrl => string.Empty;
}

public partial interface IGetDiscDetail_MediaItems_Nodes : IDisplayItem
{
}

public partial interface IGetDiscDetail_MediaItems_Nodes_Releases : IDisplayItem
{
}

public partial interface IGetDiscDetail_MediaItems_Nodes_Releases_Discs : IDisc
{
}

public partial interface IGetDiscDetail_MediaItems_Nodes_Releases_Discs_Titles : IDiscItem
{
}

public partial interface IGetBoxsetDiscs_Boxsets_Nodes_Release_Discs_Titles : IDiscItem
{
}

public partial class GetBoxsetDiscs_Boxsets_Nodes_Release_Discs_Titles_Title
{
    public string Description => this.Item?.Title ?? string.Empty;
    public string ItemType => this.Item?.Type ?? string.Empty;
    public string Season => this.Item?.Season ?? string.Empty;
    public string Episode => this.Item?.Episode ?? string.Empty;
    public bool HasItem => this.Item != null;
}

public partial class GetDiscDetail_MediaItems_Nodes_Releases_Discs_Titles_Title
{
    public string Description => this.Item?.Title ?? string.Empty;
    public string ItemType => this.Item?.Type ?? string.Empty;
    public string Season => this.Item?.Season ?? string.Empty;
    public string Episode => this.Item?.Episode ?? string.Empty;
    public bool HasItem => this.Item != null;
}

public partial interface IGetBoxsetDiscs_Boxsets_Nodes : IDisplayItem
{
}

public partial interface IGetBoxsetDiscs_Boxsets_Nodes_Release : IDisplayItem
{
}

public partial interface IGetMediaItemsByGroup_MediaItemsByGroup_Nodes : IDisplayItem
{
}

public partial interface IGetBoxsetDiscs_Boxsets_Nodes_Release_Discs : IDisc
{
}

public partial class GetBoxsetDiscs_Boxsets_Nodes_Boxset
{
}

public partial interface IGetMediaItemsByGroup_MediaItemsByGroup_PageInfo : IPageInfo
{
}

public partial interface IGetSeries_MediaItems_PageInfo : IPageInfo
{
}

public partial interface IGetMovies_MediaItems_PageInfo : IPageInfo
{
}

public partial interface IGetBoxsets_Boxsets_PageInfo : IPageInfo
{
}

public interface IGraphQlQuery<TResult>
    where TResult : class
{
    Task<IOperationResult<TResult>> ExecuteAsync(string? after, IReadOnlyList<MediaItemSortInput>? order, CancellationToken cancellationToken = default);
}