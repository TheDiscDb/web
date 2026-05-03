using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

public class EditBoxsetRequest
{
    [Required]
    public string Title { get; set; } = string.Empty;
    public string? SortTitle { get; set; }
    [Required]
    public string Slug { get; set; } = string.Empty;
    public string? Asin { get; set; }
    public string? Upc { get; set; }
    public string? Locale { get; set; }
    public string? RegionCode { get; set; }
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;
}

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class BoxsetEdit : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    [Parameter]
    public string BoxsetId { get; set; } = string.Empty;

    private SqlServerDataContext database = default!;
    private UserContributionBoxset? Boxset;
    private DateTime? releaseDate;
    private readonly EditBoxsetRequest request = new();
    private readonly UserContributionStatus[] statusList = Enum.GetValues<UserContributionStatus>();

    private string? imageMessage;
    private bool imageMessageIsError;
    private long imageVersion;

    private string? saveMessage;
    private bool saveMessageIsError;

    private string? memberMessage;
    private bool memberMessageIsError;

    private bool showRemoveDialog;
    private int? removingMemberId;

    // Working list the table iterates and the drag handlers mutate. Kept in sync with
    // Boxset.Members on load and on remove. Saved as new SortOrder on drag end.
    private List<UserContributionBoxsetMember> orderedMembers = new();
    private UserContributionBoxsetMember? draggedMember;
    private UserContributionBoxsetMember? dragOverMember;
    private bool isReorderSaving;

    private IStaticAssetStore ImageStore => ServiceProvider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);
    private IStaticAssetStore AssetStore => ServiceProvider.GetRequiredService<IStaticAssetStore>();

    protected override async Task OnInitializedAsync()
    {
        this.database = await DbFactory.CreateDbContextAsync();
        await LoadBoxset();
    }

    private async Task LoadBoxset()
    {
        var decodedId = IdEncoder.Decode(BoxsetId);
        Boxset = await database.UserContributionBoxsets
            .Include(b => b.Members)
                .ThenInclude(m => m.Disc!)
                    .ThenInclude(d => d.UserContribution)
            .FirstOrDefaultAsync(b => b.Id == decodedId);

        if (Boxset != null)
        {
            request.Title = Boxset.Title;
            request.SortTitle = Boxset.SortTitle;
            request.Slug = Boxset.Slug;
            request.Asin = Boxset.Asin;
            request.Upc = Boxset.Upc;
            request.Locale = Boxset.Locale;
            request.RegionCode = Boxset.RegionCode;
            request.Status = Boxset.Status;
            releaseDate = Boxset.ReleaseDate?.UtcDateTime.Date;
            orderedMembers = Boxset.Members.OrderBy(m => m.SortOrder).ToList();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (database != null)
        {
            await database.DisposeAsync();
        }
    }

    private async Task HandleValidSubmit()
    {
        if (Boxset == null) return;

        saveMessage = null;
        saveMessageIsError = false;

        try
        {
            var oldStatus = Boxset.Status;
            var newStatus = request.Status;

            Boxset.Title = request.Title;
            Boxset.SortTitle = !string.IsNullOrWhiteSpace(request.SortTitle) ? request.SortTitle : GenerateSortTitle(request.Title);
            Boxset.Slug = request.Slug;
            Boxset.Asin = request.Asin;
            Boxset.Upc = request.Upc;
            Boxset.Locale = request.Locale;
            Boxset.RegionCode = request.RegionCode;
            Boxset.Status = newStatus;
            Boxset.ReleaseDate = releaseDate.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(releaseDate.Value, DateTimeKind.Utc))
                : null;

            // Cascade status to member contributions to stay consistent with the BoxsetDetails
            // workflow buttons (Approve / MarkAsImported / Request Changes / Reject).
            if (oldStatus != newStatus)
            {
                foreach (var member in Boxset.Members)
                {
                    if (member.Disc?.UserContribution != null)
                    {
                        member.Disc.UserContribution.Status = newStatus;
                    }
                }
            }

            await database.SaveChangesAsync();
            saveMessage = oldStatus != newStatus
                ? $"Boxset saved. Status of {Boxset.Members.Count(m => m.Disc?.UserContribution != null)} member contribution(s) also set to {newStatus}."
                : "Boxset metadata saved.";
        }
        catch (Exception ex)
        {
            saveMessage = $"Failed to save: {ex.Message}";
            saveMessageIsError = true;
        }
    }

    private static string GenerateSortTitle(string title)
    {
        if (title.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
        {
            return title[4..].Trim() + ", The";
        }
        return title;
    }

    private async Task OnFrontImageSelected(InputFileChangeEventArgs args) => await UploadImageFromFile(args.File, "front");

    private async Task OnBackImageSelected(InputFileChangeEventArgs args) => await UploadImageFromFile(args.File, "back");

    private async Task UploadImageFromFile(IBrowserFile file, string name)
    {
        imageMessage = null;
        imageMessageIsError = false;

        if (Boxset == null || file == null) return;

        try
        {
            string encodedId = IdEncoder.Encode(Boxset.Id);
            string imageStorePath = $"Contributions/Boxsets/{encodedId}/{name}.jpg";
            string assetStorePath = $"Boxsets/{encodedId}/{name}.jpg";

            // Delete existing blobs first — Save() skips upload if blob already exists
            await ImageStore.Delete(imageStorePath, default);
            await AssetStore.Delete(assetStorePath, default);

            using var memoryStream = new MemoryStream();
            await using var fileStream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
            await fileStream.CopyToAsync(memoryStream);

            memoryStream.Position = 0;
            await ImageStore.Save(memoryStream, imageStorePath, ContentTypes.ImageContentType, default);

            memoryStream.Position = 0;
            await AssetStore.Save(memoryStream, assetStorePath, ContentTypes.ImageContentType, default);

            string imageUrl = $"/api/contribute/images/Contributions/Boxsets/{encodedId}/{name}.jpg";
            if (name == "front")
                Boxset.FrontImageUrl = imageUrl;
            else
                Boxset.BackImageUrl = imageUrl;

            await database.SaveChangesAsync();
            imageVersion = DateTimeOffset.UtcNow.Ticks;
            imageMessage = $"{(name == "front" ? "Front" : "Back")} image updated.";
        }
        catch (Exception ex)
        {
            imageMessage = $"Failed to upload {name} image: {ex.Message}";
            imageMessageIsError = true;
        }

        StateHasChanged();
    }

    private async Task DeleteImage(string name)
    {
        imageMessage = null;
        imageMessageIsError = false;

        if (Boxset == null) return;

        try
        {
            string encodedId = IdEncoder.Encode(Boxset.Id);
            await ImageStore.Delete($"Contributions/Boxsets/{encodedId}/{name}.jpg", default);
            await AssetStore.Delete($"Boxsets/{encodedId}/{name}.jpg", default);

            if (name == "front")
                Boxset.FrontImageUrl = null;
            else
                Boxset.BackImageUrl = null;

            await database.SaveChangesAsync();
            imageMessage = $"{(name == "front" ? "Front" : "Back")} image deleted.";
        }
        catch (Exception ex)
        {
            imageMessage = $"Failed to delete {name} image: {ex.Message}";
            imageMessageIsError = true;
        }
    }

    private void OnDragStart(UserContributionBoxsetMember member)
    {
        if (isReorderSaving) return;
        draggedMember = member;
    }

    private void OnDragEnter(UserContributionBoxsetMember member)
    {
        if (draggedMember == null || member == draggedMember || isReorderSaving) return;
        dragOverMember = member;

        int fromIndex = orderedMembers.IndexOf(draggedMember);
        int toIndex = orderedMembers.IndexOf(member);
        if (fromIndex < 0 || toIndex < 0) return;

        orderedMembers.RemoveAt(fromIndex);
        orderedMembers.Insert(toIndex, draggedMember);
    }

    private async Task OnDragEnd()
    {
        var wasDragging = draggedMember != null;
        draggedMember = null;
        dragOverMember = null;

        if (!wasDragging || isReorderSaving || Boxset == null) return;

        isReorderSaving = true;
        memberMessage = null;
        memberMessageIsError = false;
        try
        {
            // Persist the user-arranged order as new SortOrder values.
            for (int i = 0; i < orderedMembers.Count; i++)
            {
                orderedMembers[i].SortOrder = i;
            }
            await database.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            memberMessage = $"Failed to reorder: {ex.Message}";
            memberMessageIsError = true;
        }
        finally
        {
            isReorderSaving = false;
        }
    }

    private string GetRowClass(UserContributionBoxsetMember member)
    {
        if (member == draggedMember) return "dragging";
        if (member == dragOverMember) return "drag-over";
        return string.Empty;
    }

    private void ConfirmRemoveMember(int memberId)
    {
        removingMemberId = memberId;
        showRemoveDialog = true;
    }

    private void CancelRemove()
    {
        showRemoveDialog = false;
        removingMemberId = null;
    }

    private async Task ExecuteRemove()
    {
        if (Boxset == null || removingMemberId == null) return;

        memberMessage = null;
        memberMessageIsError = false;

        try
        {
            var member = Boxset.Members.FirstOrDefault(m => m.Id == removingMemberId.Value);
            if (member != null)
            {
                database.UserContributionBoxsetMembers.Remove(member);
                Boxset.Members.Remove(member);
                orderedMembers.Remove(member);

                // Renumber remaining members so the SortOrder sequence stays dense.
                for (int i = 0; i < orderedMembers.Count; i++)
                {
                    orderedMembers[i].SortOrder = i;
                }

                await database.SaveChangesAsync();
                memberMessage = "Disc removed from boxset.";
            }
        }
        catch (Exception ex)
        {
            memberMessage = $"Failed to remove disc: {ex.Message}";
            memberMessageIsError = true;
        }
        finally
        {
            showRemoveDialog = false;
            removingMemberId = null;
        }
    }

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
    private (string Text, string Url) GetBoxsetDetailsLink() => (Boxset?.Title ?? "Boxset", $"/admin/boxset/{BoxsetId}");
}
