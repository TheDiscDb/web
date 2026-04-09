using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Services.Server;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Components.Pages.Admin;

public class EditContributionRequest
{
    [Required]
    public DateTimeOffset ReleaseDate { get; set; }
    [Required]
    [RegularExpression(@"\w{10}", ErrorMessage = "ASIN must be a combination 10 characters or numbers")]
    public string Asin { get; set; } = string.Empty;
    [Required]
    [RegularExpression(@"\d{12,13}", ErrorMessage = "UPC must be exactly 12 digits")]
    public string Upc { get; set; } = string.Empty;
    public string ReleaseTitle { get; set; } = string.Empty;
    [Required]
    public string ReleaseSlug { get; set; } = string.Empty;
    [Required]
    public string RegionCode { get; set; } = string.Empty;
    [Required]
    public string Locale { get; set; } = string.Empty;
    public UserContributionStatus Status { get; set; } = UserContributionStatus.Pending;
}

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class ContributionEdit : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IDbContextFactory<SqlServerDataContext> DbFactory { get; set; } = null!;

    [Inject]
    private UserManager<TheDiscDbUser> UserManager { get; set; } = null!;

    [Inject]
    private IdEncoder IdEncoder { get; set; } = null!;

    [Inject]
    private IContributionHistoryService HistoryService { get; set; } = null!;

    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = null!;

    [Parameter]
    public string? ContributionId { get; set; }

    private SqlServerDataContext database = default!;
    private UserContribution? Contribution { get; set; }
    private IQueryable<UserContributionDisc>? Discs => Contribution?.Discs.AsQueryable();
    private TheDiscDbUser? User { get; set; }
    private readonly List<UserContributionStatus> statusList = Enum.GetValues<UserContributionStatus>().ToList();

    private readonly EditContributionRequest request = new EditContributionRequest
    {
    };

    private string? imageMessage;
    private bool imageMessageIsError;

    private IStaticAssetStore ImageStore => ServiceProvider.GetRequiredKeyedService<IStaticAssetStore>(KeyedServiceNames.ImagesAssetStore);
    private IStaticAssetStore AssetStore => ServiceProvider.GetRequiredService<IStaticAssetStore>();

    protected override async Task OnInitializedAsync()
    {
        if (DbFactory != null)
        {
            this.database = await DbFactory.CreateDbContextAsync();

            this.Contribution = await database.UserContributions
                .Include(c => c.Discs)
                .ThenInclude(d => d.Items)
                .FirstOrDefaultAsync(uc => uc.Id.ToString() == ContributionId);

            if (this.Contribution != null)
            {
                request.Asin = this.Contribution.Asin ?? string.Empty;
                request.Locale = this.Contribution.Locale ?? string.Empty;
                request.RegionCode = this.Contribution.RegionCode ?? string.Empty;
                request.ReleaseDate = this.Contribution.ReleaseDate;
                request.ReleaseSlug = this.Contribution.ReleaseSlug ?? string.Empty;
                request.ReleaseTitle = this.Contribution.ReleaseTitle ?? string.Empty;
                request.Upc = this.Contribution.Upc ?? string.Empty;
                request.Status = this.Contribution.Status;

                this.IdEncoder.EncodeInPlace(this.Contribution);

                if (!string.IsNullOrEmpty(this.Contribution?.UserId))
                {
                    this.User = await UserManager.FindByIdAsync(this.Contribution.UserId);
                }
            }
        }
    }

    public async ValueTask DisposeAsync() => await database.DisposeAsync();

    private async Task HandleValidSubmit(Microsoft.AspNetCore.Components.Forms.EditContext args)
    {
        if (this.Contribution != null)
        {
            var oldStatus = this.Contribution.Status;

            this.Contribution.Asin = request.Asin;
            this.Contribution.Locale = request.Locale;
            this.Contribution.RegionCode = request.RegionCode;
            this.Contribution.ReleaseDate = request.ReleaseDate;
            this.Contribution.ReleaseSlug = request.ReleaseSlug;
            this.Contribution.ReleaseTitle = request.ReleaseTitle;
            this.Contribution.Upc = request.Upc;
            this.Contribution.Status = request.Status;
            await database.SaveChangesAsync();

            if (oldStatus != request.Status)
            {
                await HistoryService.RecordStatusChangedAsync(this.Contribution.Id, this.Contribution.UserId, oldStatus, request.Status);
            }
        }
    }

    private async Task OnFrontImageSelected(InputFileChangeEventArgs args)
    {
        await UploadImageFromFile(args.File, "front");
    }

    private async Task OnBackImageSelected(InputFileChangeEventArgs args)
    {
        await UploadImageFromFile(args.File, "back");
    }

    private async Task UploadImageFromFile(Microsoft.AspNetCore.Components.Forms.IBrowserFile file, string name)
    {
        imageMessage = null;
        imageMessageIsError = false;

        if (this.Contribution == null || file == null)
            return;

        try
        {
            string encodedId = this.Contribution.EncodedId;
            string imageStorePath = $"Contributions/{encodedId}/{name}.jpg";
            string assetStorePath = $"{encodedId}/{name}.jpg";

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

            string imageUrl = $"/images/Contributions/{encodedId}/{name}.jpg";
            if (name == "front")
                this.Contribution.FrontImageUrl = imageUrl;
            else
                this.Contribution.BackImageUrl = imageUrl;

            await database.SaveChangesAsync();
            imageMessage = $"{(name == "front" ? "Front" : "Back")} image updated. The preview may take a moment to refresh.";
        }
        catch (Exception ex)
        {
            imageMessage = $"Failed to upload {name} image: {ex.Message}";
            imageMessageIsError = true;
        }

        StateHasChanged();
    }

    private async Task DeleteBackImage()
    {
        imageMessage = null;
        imageMessageIsError = false;

        if (this.Contribution == null)
            return;

        try
        {
            string encodedId = this.Contribution.EncodedId;
            await ImageStore.Delete($"Contributions/{encodedId}/back.jpg", default);
            await AssetStore.Delete($"{encodedId}/back.jpg", default);

            this.Contribution.BackImageUrl = null;
            await database.SaveChangesAsync();

            imageMessage = "Back image deleted.";
        }
        catch (Exception ex)
        {
            imageMessage = $"Failed to delete back image: {ex.Message}";
            imageMessageIsError = true;
        }
    }
}
