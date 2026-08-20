namespace TheDiscDb.Components.Pages.Admin;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Services.Admin;
using TheDiscDb.Web.Data;

[Authorize(Roles = DefaultRoles.Administrator)]
public partial class IntakeDetails : ComponentBase
{
    [Inject]
    private IIntakeAdminService IntakeAdminService { get; set; } = null!;

    [Parameter]
    public int Id { get; set; }

    private IntakeReleaseDetails? Details { get; set; }
    private bool showDeleteDialog;
    private readonly List<string> deleteMessages = [];
    private readonly List<string> deleteWarnings = [];
    private bool deleteHasError;
    private bool releaseDeleted;

    protected override async Task OnParametersSetAsync()
    {
        showDeleteDialog = false;
        deleteMessages.Clear();
        deleteWarnings.Clear();
        deleteHasError = false;
        releaseDeleted = false;
        Details = await IntakeAdminService.GetReleaseAsync(Id);
    }

    private void ShowDeleteDialog() => showDeleteDialog = true;

    private void HideDeleteDialog() => showDeleteDialog = false;

    private async Task DeleteAsync()
    {
        if (Details == null || releaseDeleted)
        {
            return;
        }

        showDeleteDialog = false;
        deleteMessages.Clear();
        deleteWarnings.Clear();
        deleteHasError = false;

        try
        {
            var result = await IntakeAdminService.DeleteReleaseAsync(Details.Id);
            deleteWarnings.AddRange(result.Warnings);

            if (result.Succeeded)
            {
                releaseDeleted = true;
                deleteMessages.Add(
                    $"Deleted {(Details.ReleaseTitle ?? Details.ReleaseSlug ?? "this intake release")}. " +
                    $"{result.DiscCount} disc link(s) removed and {result.OrphanDiscCount} orphaned disc(s) deleted.");
                return;
            }

            deleteHasError = true;
            deleteMessages.AddRange(result.Errors.Count == 0
                ? ["Failed to delete the intake release."]
                : result.Errors);
        }
        catch (Exception exception)
        {
            deleteHasError = true;
            deleteMessages.Add($"Failed to delete the intake release: {exception.Message}");
        }
    }

    private static string GetImageUrl(string location) =>
        location.StartsWith("/", StringComparison.Ordinal) ? location : $"/images/{location}";

    private static (string Text, string Url) GetRootAdminLink() => ("Admin", "/admin");
    private static (string Text, string Url) GetIntakeLink() => ("Intake", "/admin/intake");
}
