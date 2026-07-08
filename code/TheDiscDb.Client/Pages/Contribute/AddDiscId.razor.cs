using KristofferStrube.Blazor.FileSystem;
using KristofferStrube.Blazor.FileSystemAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using StrawberryShake;
using TheDiscDb.Client.Contributions;

namespace TheDiscDb.Client.Pages.Contribute;

[Authorize]
public partial class AddDiscId : CancellableComponentBase
{
    [Inject]
    public IFileSystemAccessServiceInProcess FileSystemAccessService { get; set; } = default!;

    [Inject]
    public IContributionClient ContributionClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "media")]
    public string? MediaItemSlug { get; set; }

    [SupplyParameterFromQuery(Name = "boxset")]
    public string? BoxsetSlug { get; set; }

    [SupplyParameterFromQuery(Name = "release")]
    public string? ReleaseSlug { get; set; }

    [SupplyParameterFromQuery(Name = "disc")]
    public string? DiscSlug { get; set; }

    [SupplyParameterFromQuery(Name = "index")]
    public int? DiscIndex { get; set; }

    [SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    // True when we arrived from a specific disc's page ("Help add it").
    private bool HasTarget => !string.IsNullOrWhiteSpace(ReleaseSlug)
        && (!string.IsNullOrWhiteSpace(MediaItemSlug) || !string.IsNullOrWhiteSpace(BoxsetSlug));

    private bool isBusy;
    private string? message;
    private string? resultClass;
    private string? computedDiscId;
    private string? existingDiscId;
    private string? matchedDiscUrl;
    private string? returnDiscUrl;

    private async Task OpenFolderAsync()
    {
        Reset();
        isBusy = true;
        try
        {
            FileSystemDirectoryHandleInProcess handle;
            try
            {
                handle = await FileSystemAccessService.ShowDirectoryPickerAsync(
                    new DirectoryPickerOptionsStartInFileSystemHandle
                    {
                        Mode = FileSystemPermissionMode.Read,
                    });
            }
            catch (Exception)
            {
                // User cancelled the picker.
                return;
            }

            var scan = await DiscScanner.ScanAsync(handle);
            if (!string.IsNullOrEmpty(scan.Error))
            {
                SetResult("error", scan.Error);
                return;
            }

            computedDiscId = scan.GlobalDiscId;
            if (string.IsNullOrEmpty(scan.GlobalDiscId))
            {
                SetResult("warn",
                    "This disc/backup has no AACS or DVD Disc ID (for example an MKV-only rip). There's nothing to submit.");
                return;
            }

            if (scan.HashFiles.Count == 0)
            {
                SetResult("error", "No hashable files were found on this disc.");
                return;
            }

            await SubmitAsync(scan);
        }
        catch (Exception ex)
        {
            SetResult("error", $"Something went wrong: {ex.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task SubmitAsync(DiscScanResult scan)
    {
        var input = new AttachGlobalDiscIdInput
        {
            GlobalDiscId = scan.GlobalDiscId!,
            Files = scan.HashFiles
                .Select(f => new FileHashInfoInput
                {
                    Index = f.Index,
                    Name = f.Name,
                    Size = f.Size,
                    CreationTime = f.CreationTime,
                })
                .ToList(),
        };

        // When we arrived from a specific disc's "Help add it", target that disc so the server can warn if the inserted disc doesn't match it.
        if (HasTarget)
        {
            input.MediaItemSlug = MediaItemSlug;
            input.BoxsetSlug = BoxsetSlug;
            input.ReleaseSlug = ReleaseSlug;
            input.DiscSlug = DiscSlug;
            input.DiscIndex = DiscIndex;
        }

        var response = await ContributionClient.AttachGlobalDiscId.ExecuteAsync(input, this.CancellationToken);

        if (!response.IsSuccessResult() || response.Data?.AttachGlobalDiscId?.AttachDiscIdResult is null)
        {
            SetResult("error", "We couldn't submit the Disc ID. Please try again.");
            return;
        }

        var result = response.Data.AttachGlobalDiscId.AttachDiscIdResult;
        existingDiscId = result.ExistingGlobalDiscId;

        switch (result.Outcome)
        {
            case AttachDiscIdOutcome.Applied when result.MatchedDifferentDisc:
                matchedDiscUrl = BuildDiscUrl(result.MediaItemSlug, result.BoxsetSlug, result.MediaItemType, result.ReleaseSlug, result.DiscSlug, result.DiscIndex);
                returnDiscUrl = ValidReturnUrl();
                SetResult("success",
                    "The disc you inserted isn't the disc you started from, but it matched another disc in our "
                    + "database that needed a Disc ID — so we've added it. You can view that disc below, insert "
                    + "the correct disc to continue, or go back to the disc you started from.");
                break;
            case AttachDiscIdOutcome.AlreadyRecorded when result.MatchedDifferentDisc:
                matchedDiscUrl = BuildDiscUrl(result.MediaItemSlug, result.BoxsetSlug, result.MediaItemType, result.ReleaseSlug, result.DiscSlug, result.DiscIndex);
                returnDiscUrl = ValidReturnUrl();
                SetResult("warn",
                    "Heads up: the disc you inserted isn't the disc you started from — and it already has a Disc ID "
                    + "recorded in our database, so there's nothing to add. Insert the correct disc to continue, or "
                    + "go back to the disc you started from.");
                break;
            case AttachDiscIdOutcome.Conflict when result.MatchedDifferentDisc:
                matchedDiscUrl = BuildDiscUrl(result.MediaItemSlug, result.BoxsetSlug, result.MediaItemType, result.ReleaseSlug, result.DiscSlug, result.DiscIndex);
                returnDiscUrl = ValidReturnUrl();
                SetResult("warn",
                    "Heads up: the disc you inserted isn't the disc you started from — and it already has a "
                    + "different Disc ID in our database. Your submission has been flagged for review. Insert the "
                    + "correct disc to continue, or go back to the disc you started from.");
                break;
            case AttachDiscIdOutcome.Applied:
                returnDiscUrl = ValidReturnUrl();
                SetResult("success",
                    "Thank you! You've added this disc's Disc ID to the database — it'll show on the disc page now.");
                break;
            case AttachDiscIdOutcome.AlreadyRecorded:
                returnDiscUrl = ValidReturnUrl();
                SetResult("success", "This disc already has this Disc ID recorded — no change needed. Thanks for checking!");
                break;
            case AttachDiscIdOutcome.Conflict:
                SetResult("warn",
                    "The matching disc already has a different Disc ID (possibly a re-press or variant). "
                    + "Your submission has been flagged for review.");
                break;
            case AttachDiscIdOutcome.Mismatch:
                SetResult("warn",
                    "The disc you inserted doesn't match the disc you selected, and we couldn't find it in the "
                    + "database either. Please insert the correct disc and try again.");
                break;
            case AttachDiscIdOutcome.NotFound:
                SetResult("warn",
                    "We couldn't find a matching disc in the database for this disc — it may not be in the database yet.");
                break;
            default:
                SetResult("error", "Unexpected response.");
                break;
        }
    }

    // The URL back to the disc the user started from (the CTA source), when present and safe.
    private string? ValidReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && ReturnUrl.StartsWith('/') ? ReturnUrl : null;

    // Builds a disc-detail link from the identity of the disc we actually updated.
    private static string? BuildDiscUrl(string? mediaItemSlug, string? boxsetSlug, string? mediaItemType, string? releaseSlug, string? discSlug, int? discIndex)
    {
        if (string.IsNullOrWhiteSpace(releaseSlug))
        {
            return null;
        }

        var discSegment = !string.IsNullOrWhiteSpace(discSlug) ? discSlug : discIndex?.ToString();
        if (string.IsNullOrWhiteSpace(discSegment))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(boxsetSlug))
        {
            return $"/boxset/{boxsetSlug}/releases/{releaseSlug}/discs/{discSegment}";
        }

        if (!string.IsNullOrWhiteSpace(mediaItemSlug) && !string.IsNullOrWhiteSpace(mediaItemType))
        {
            return $"/{mediaItemType!.ToLowerInvariant()}/{mediaItemSlug}/releases/{releaseSlug}/discs/{discSegment}";
        }

        return null;
    }

    private void Reset()
    {
        message = null;
        resultClass = null;
        computedDiscId = null;
        existingDiscId = null;
        matchedDiscUrl = null;
        returnDiscUrl = null;
    }

    private void SetResult(string cssClass, string text)
    {
        resultClass = cssClass;
        message = text;
    }
}
