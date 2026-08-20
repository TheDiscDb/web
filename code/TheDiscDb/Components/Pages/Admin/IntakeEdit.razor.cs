namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using TheDiscDb.Client.Interop;
using TheDiscDb.InputModels;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class IntakeEdit : ComponentBase, IDisposable
{
    private const long MaxImageBytes = 10 * 1024 * 1024;

    [Inject]
    private IIntakeAdminService IntakeAdminService { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Parameter]
    public int Id { get; set; }

    private readonly IntakeStatus[] statuses = Enum.GetValues<IntakeStatus>();
    private readonly string[] formats = [.. DiscFormatConstants.ContributionFormats];
    private IntakeReleaseDetails? Details { get; set; }
    private IntakeReleaseEditRequest releaseRequest = new();
    private List<IntakeDiscLinkEditRequest> discRequests = [];
    private string? releaseDateText;
    private bool confirmSharedFormatChange;
    private List<string> operationMessages = [];
    private List<string> operationWarnings = [];
    private bool operationHasError;
    private IntakeDiscLinkEditRequest? draggedDisc;
    private IntakeDiscLinkEditRequest? dragOverDisc;
    private ElementReference tableBodyRef;
    private TouchSortable<IntakeDiscLinkEditRequest>? sortable;

    protected override async Task OnParametersSetAsync()
    {
        operationMessages = [];
        operationWarnings = [];
        operationHasError = false;
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Details == null || discRequests.Count == 0)
        {
            return;
        }

        sortable ??= new TouchSortable<IntakeDiscLinkEditRequest>(
            JS,
            () => discRequests,
            OnDragStart,
            OnDragEnter,
            OnDragEnd,
            StateHasChanged);
        await sortable.InitAsync(tableBodyRef);
    }

    private async Task LoadAsync()
    {
        Details = await IntakeAdminService.GetReleaseAsync(Id);
        if (Details == null)
        {
            return;
        }

        releaseRequest = IntakeReleaseEditRequest.FromDetails(Details);
        releaseDateText = Details.ReleaseDate?.ToString("yyyy-MM-dd");
        discRequests = Details.Discs.Select(IntakeDiscLinkEditRequest.FromDetails).ToList();
        confirmSharedFormatChange = false;
    }

    private async Task SaveReleaseAsync()
    {
        if (!TrySetReleaseDate())
        {
            return;
        }

        try
        {
            var result = await IntakeAdminService.UpdateReleaseAsync(Id, releaseRequest);
            SetResult(result);
            if (result.Succeeded)
            {
                await LoadAsync();
            }
        }
        catch (Exception exception)
        {
            SetOperationException("Failed to save release metadata", exception);
        }
    }

    private async Task SaveDiscsAsync()
    {
        try
        {
            var result = await IntakeAdminService.UpdateDiscsAsync(Id, discRequests, confirmSharedFormatChange);
            SetResult(result);
            if (result.Succeeded)
            {
                await LoadAsync();
            }
        }
        catch (Exception exception)
        {
            SetOperationException("Failed to save disc metadata", exception);
        }
    }

    private async Task UploadImageAsync(IBrowserFile file, IntakeImageSide side)
    {
        string sideName = side == IntakeImageSide.Front ? "front" : "back";
        try
        {
            await using var stream = file.OpenReadStream(MaxImageBytes);
            var result = await IntakeAdminService.ReplaceImageAsync(Id, side, stream, file.Name, file.ContentType);
            SetResult(result);
            if (result.Succeeded)
            {
                await LoadAsync();
            }
        }
        catch (Exception exception)
        {
            SetOperationException($"Failed to upload {sideName} image", exception);
        }
    }

    private Task OnFrontImageSelected(InputFileChangeEventArgs args) =>
        UploadImageAsync(args.File, IntakeImageSide.Front);

    private Task OnBackImageSelected(InputFileChangeEventArgs args) =>
        UploadImageAsync(args.File, IntakeImageSide.Back);

    private async Task DeleteImageAsync(IntakeImageSide side)
    {
        string sideName = side == IntakeImageSide.Front ? "front" : "back";
        try
        {
            var result = await IntakeAdminService.DeleteImageAsync(Id, side);
            SetResult(result);
            if (result.Succeeded)
            {
                await LoadAsync();
            }
        }
        catch (Exception exception)
        {
            SetOperationException($"Failed to delete {sideName} image", exception);
        }
    }

    private bool TrySetReleaseDate()
    {
        if (string.IsNullOrWhiteSpace(releaseDateText))
        {
            releaseRequest.ReleaseDate = null;
            return true;
        }

        if (!DateOnly.TryParse(releaseDateText, out var date))
        {
            operationHasError = true;
            operationMessages = ["Release date must be a valid date."];
            operationWarnings = [];
            return false;
        }

        releaseRequest.ReleaseDate = new DateTimeOffset(
            DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
        return true;
    }

    private void OnDragStart(IntakeDiscLinkEditRequest disc)
    {
        draggedDisc = disc;
    }

    private void OnDragEnter(IntakeDiscLinkEditRequest disc)
    {
        if (draggedDisc == null || draggedDisc == disc)
        {
            return;
        }

        dragOverDisc = disc;
        int fromIndex = discRequests.IndexOf(draggedDisc);
        int toIndex = discRequests.IndexOf(disc);
        if (fromIndex < 0 || toIndex < 0)
        {
            return;
        }

        discRequests.RemoveAt(fromIndex);
        discRequests.Insert(toIndex, draggedDisc);
    }

    private Task OnDragEnd()
    {
        draggedDisc = null;
        dragOverDisc = null;
        for (int index = 0; index < discRequests.Count; index++)
        {
            discRequests[index].Index = index + 1;
        }

        return Task.CompletedTask;
    }

    private string GetRowClass(IntakeDiscLinkEditRequest disc)
    {
        if (draggedDisc == disc)
        {
            return "table-info";
        }

        return dragOverDisc == disc ? "table-active" : string.Empty;
    }

    private IntakeDiscLinkDetails? GetDiscDetails(int linkId) =>
        Details?.Discs.FirstOrDefault(disc => disc.LinkId == linkId);

    private IntakeEvidenceDetails? GetBackImage() =>
        Details?.ReleaseImages
            .Where(image => image.Type == IntakeDiscEvidenceType.BackImage)
            .OrderByDescending(image => image.RecordedAt)
            .FirstOrDefault();

    private void SetResult(IntakeAdminResult result)
    {
        operationHasError = !result.Succeeded;
        operationMessages = result.Succeeded
            ? [result.Message ?? "Saved."]
            : result.Errors.ToList();
        operationWarnings = result.Warnings.ToList();
    }

    private void SetOperationException(string message, Exception exception)
    {
        operationHasError = true;
        operationMessages = [$"{message}: {exception.Message}"];
        operationWarnings = [];
    }

    private static string GetImageUrl(string location) =>
        location.StartsWith("/", StringComparison.Ordinal) ? location : $"/images/{location}";

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
    private static (string Text, string Url) GetIntakeLink() => ("Intake", "/admin/intake");
    private (string Text, string Url) GetDetailsLink() =>
        (Details?.ReleaseTitle ?? Details?.ReleaseSlug ?? "Details", $"/admin/intake/{Id}");

    public void Dispose()
    {
        sortable?.Dispose();
    }
}
