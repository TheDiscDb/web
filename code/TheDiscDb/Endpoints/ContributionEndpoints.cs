using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp.Formats.Gif;
using Sqids;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web;

public class ContributionEndpoints
{
    public void MapEndpoints(WebApplication app)
    {
        var contribute = app.MapGroup("/api/contribute").RequireAuthorization();

        contribute.MapGet("", GetUserContributions);
        contribute.MapPost("create", CreateContribution);
        contribute.MapGet("{contributionId}", GetContribution);
        contribute.MapDelete("{contributionId}", DeleteContribution);
        contribute.MapPut("{contributionId}", UpdateContribution);
        contribute.MapPost("{contributionId}/hashdisc", HashDisc);

        contribute.MapGet("{contributionId}/discs", GetDiscs);
        contribute.MapPost("{contributionId}/discs/{discId}/logs", SaveDiscLogs)
            .AllowAnonymous()
            .Accepts<string>("text/plain");
        contribute.MapGet("{contributionId}/discs/{discId}/logs", GetDiscLogs);
        contribute.MapPost("{contributionId}/discs/create", CreateDisc);
        contribute.MapPut("{contributionId}/discs/{discId}", UpdateDisc);
        contribute.MapDelete("{contributionId}/discs/{discId}", DeleteDisc);
        contribute.MapGet("checkdiskuploadstatus/{discId}", CheckDiskUploadStatus)
            .AllowAnonymous();

        contribute.MapPost("{contributionId}/discs/{discId}/items", AddItemToDisc);
        contribute.MapDelete("{contributionId}/discs/{discId}/items/{itemId}", DeleteItemFromDisc);

        contribute.MapPost("{contributionId}/discs/{discId}/items/{itemId}/chapters", AddChapterToItem);
        contribute.MapDelete("{contributionId}/discs/{discId}/items/{itemId}/chapters/{chapterId}", DeleteChapterFromItem);
        contribute.MapPut("{contributionId}/discs/{discId}/items/{itemId}/chapters/{chapterId}", UpdateChapterInItem);

        contribute.MapPost("{contributionId}/discs/{discId}/items/{itemId}/audiotracks", AddAudioTrackToItem);
        contribute.MapDelete("{contributionId}/discs/{discId}/items/{itemId}/audiotracks/{audioTrackId}", DeleteAudioTrackFromItem);
        contribute.MapPut("{contributionId}/discs/{discId}/items/{itemId}/audiotracks/{audioTrackId}", UpdateAudioTrackInItem);
    }

    public async Task<IResult> GetUserContributions(IUserContributionService service, UserManager<TheDiscDbUser> userManager, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(user);
        if (string.IsNullOrEmpty(userId))
        {
            return TypedResults.NotFound();
        }

        var result = await service.GetUserContributions(userId, cancellationToken);
        return JsonResult(result, "Failed to add item to disc");
    }

    public async Task<IResult> CreateContribution(IUserContributionService service, UserManager<TheDiscDbUser> userManager, [FromBody] CreateContributionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(user);
        //var user = await this.userManager.FindByIdAsync(userId!);
        if (string.IsNullOrEmpty(userId))
        {
            return TypedResults.NotFound();
        }

        var result = await service.CreateContribution(userId, request, cancellationToken);
        return JsonResult(result, "Failed to create contribution");
    }

    public async Task<IResult> GetContribution(IUserContributionService service, string contributionId, CancellationToken cancellationToken)
    {
        var result = await service.GetContribution(contributionId, cancellationToken);
        return JsonResult(result, "Failed to get contribution");
    }

    public async Task<IResult> DeleteContribution(IUserContributionService service, string contributionId, CancellationToken cancellationToken)
    {
        var result = await service.DeleteContribution(contributionId, cancellationToken);
        return OkOrProblem(result, $"Failed to delete contribution {contributionId}");
    }

    public async Task<IResult> UpdateContribution(IUserContributionService service, string contributionId, [FromBody] CreateContributionRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateContribution(contributionId, request, cancellationToken);
        return OkOrProblem(result, $"Failed to update contribution {contributionId}");
    }


    public async Task<IResult> GetDiscs(IUserContributionService service, string contributionId, CancellationToken cancellationToken)
    {
        var result = await service.GetDiscs(contributionId, cancellationToken);
        return JsonResult(result, $"Unable to get discs for {contributionId}");
    }

    public async Task<IResult> SaveDiscLogs(IUserContributionService service, HttpRequest request, string contributionId, string discId, CancellationToken cancellationToken)
    {
        using (StreamReader reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            string logs = await reader.ReadToEndAsync();
            var result = await service.SaveDiscLogs(contributionId, discId, logs, cancellationToken);
            // TODO: include a traceid people can share to look up problem later
            return OkOrProblem(result, $"Unable to save disc logs for contribution {contributionId}, disc {discId}");
        }
    }

    public async Task<IResult> GetDiscLogs(IUserContributionService service, string contributionId, string discId, CancellationToken cancellationToken)
    {
        var result = await service.GetDiscLogs(contributionId, discId, cancellationToken);
        return JsonResult(result, $"Unable to get disc logs for contribution {contributionId}, disc {discId}");
    }

    public async Task<IResult> CreateDisc(IUserContributionService service, string contributionId, [FromBody] SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateDisc(contributionId, request, cancellationToken);
        return JsonResult(result, $"Unable to create disc for contribution {contributionId}");
    }

    public async Task<IResult> HashDisc(IUserContributionService service, string contributionId, [FromBody] HashDiscRequest request, CancellationToken cancellation)
    {
        var result = await service.HashDisc(contributionId, request, cancellation);
        return JsonResult(result, $"Unable to calculate hash for contribution {contributionId}");
    }

    public async Task<IResult> UpdateDisc(IUserContributionService service, string contributionId, string discId, [FromBody] SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateDisc(contributionId, discId, request, cancellationToken);
        return OkOrProblem(result, $"Unable to update disc {discId} for contribution {contributionId}");
    }
    
    public async Task<IResult> DeleteDisc(IUserContributionService service, string contributionId, string discId, CancellationToken cancellationToken)
    {
        var result = await service.DeleteDisc(contributionId, discId, cancellationToken);
        return OkOrProblem(result, $"Unable to delete disc {discId} for contribution {contributionId}");
    }
    
    public async Task<IResult> CheckDiskUploadStatus(IUserContributionService service, string discId, CancellationToken cancellationToken)
    {
        var result = await service.CheckDiskUploadStatus(discId, cancellationToken);
        return JsonResult(result, $"Unable to get disc status for {discId}");
    }


    public async Task<IResult> AddItemToDisc(IUserContributionService service, string contributionId, string discId, [FromBody] AddItemRequest request, CancellationToken cancellationToken)
    {
        var result = await service.AddItemToDisc(contributionId, discId, request, cancellationToken);
        return JsonResult(result, $"Failed to add item to disc {discId} for contribution {contributionId}");
    }

    public async Task<IResult> DeleteItemFromDisc(IUserContributionService service, SqidsEncoder<int> idEncoder, string contributionId, string discId, string itemId, CancellationToken cancellationToken)
    {
        if (Int32.TryParse(itemId, out int parsedItemId))
        {
            itemId = idEncoder.Encode(parsedItemId);
        }

        var result = await service.DeleteItemFromDisc(contributionId, discId, itemId, cancellationToken);
        return OkOrProblem(result, $"Unable to delete item {itemId} from disc {discId} for contribution {contributionId}");
    }


    public async Task<IResult> AddChapterToItem(IUserContributionService service, SqidsEncoder<int> idEncoder, string contributionId, string discId, string itemId, [FromBody] AddChapterRequest request, CancellationToken cancellationToken)
    {
        if (Int32.TryParse(itemId, out int parsedItemId))
        {
            itemId = idEncoder.Encode(parsedItemId);
        }

        var result = await service.AddChapterToItem(contributionId, discId, itemId, request, cancellationToken);
        return JsonResult(result, $"Failed to add chapter item to disc {discId} for contribution {contributionId}");
    }

    public async Task<IResult> DeleteChapterFromItem(IUserContributionService service, string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken)
    {
        var result = await service.DeleteChapterFromItem(contributionId, discId, itemId, chapterId, cancellationToken);
        return OkOrProblem(result, $"Unable to delete chapter {chapterId} from item {itemId} from disc {discId} for contribution {contributionId}");
    }

    public async Task<IResult> UpdateChapterInItem(IUserContributionService service, string contributionId, string discId, string itemId, string chapterId, [FromBody] AddChapterRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateChapterInItem(contributionId, discId, itemId, chapterId, request, cancellationToken);
        return OkOrProblem(result, $"Unable to update chapter {chapterId} from item {itemId} from disc {discId} for contribution {contributionId}");
    }


    public async Task<IResult> AddAudioTrackToItem(IUserContributionService service, SqidsEncoder<int> idEncoder, string contributionId, string discId, string itemId, [FromBody] AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        if (Int32.TryParse(itemId, out int parsedItemId))
        {
            itemId = idEncoder.Encode(parsedItemId);
        }

        var result = await service.AddAudioTrackToItem(contributionId, discId, itemId, request, cancellationToken);
        return JsonResult(result, $"Failed to add audio track to disc {discId} for contribution {contributionId}");
    }

    public async Task<IResult> DeleteAudioTrackFromItem(IUserContributionService service, string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAudioTrackFromItem(contributionId, discId, itemId, audioTrackId, cancellationToken);
        return OkOrProblem(result, $"Unable to delete audio track {audioTrackId} from item {itemId} from disc {discId} for contribution {contributionId}");
    }

    public async Task<IResult> UpdateAudioTrackInItem(IUserContributionService service, string contributionId, string discId, string itemId, string audioTrackId, [FromBody] AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAudioTrackInItem(contributionId, discId, itemId, audioTrackId, request, cancellationToken);
        return OkOrProblem(result, $"Unable to update audio track {audioTrackId} from item {itemId} from disc {discId} for contribution {contributionId}");
    }

    private IResult OkOrProblem(FluentResults.Result result, string problemMessage)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }
        else
        {
            return TypedResults.Problem(problemMessage);
        }
    }

    private IResult JsonResult<T>(FluentResults.Result<T> result, string problemMessage)
    {
        if (result.IsSuccess)
        {
            return Results.Json(result.Value);
        }
        else
        {
            return TypedResults.Problem(problemMessage);
        }
    }
}
