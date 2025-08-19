﻿using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.InputModels;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages;

public partial class ReleaseDetail : ComponentBase
{
    [Parameter]
    public string? Type { get; set; }

    [Parameter]
    public string? Slug { get; set; }

    [Parameter]
    public string? ReleaseSlug { get; set; }

    [Inject]
    public CacheHelper? Cache {  get; set; }

    private MediaItem? Item { get; set; }
    private Release? Release { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (this.Cache == null)
        {
            throw new Exception("Cache was not injected");
        }

        if (this.Type != null && this.Slug != null)
        {
            this.Item = await this.Cache.GetMediaItemDetail(Type, Slug);
        }

        if (Item != null)
        {
            Release = Item.Releases.FirstOrDefault(r => r.Slug == ReleaseSlug);
        }
    }
}
