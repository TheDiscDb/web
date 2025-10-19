using FluentResults.Extensions.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sqids;
using TheDiscDb.Data.Import;
using TheDiscDb.Services;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Web.Controllers;

public class UserNotFoundResult : ObjectResult
{
    public UserNotFoundResult() : base("User not found")
    {
        this.StatusCode = StatusCodes.Status400BadRequest;
    }
}

public class ItemNotFoundResult : ObjectResult
{
    public ItemNotFoundResult(string itemId, string type) : base($"A {type} with id {itemId} not found")
    {
        this.StatusCode = StatusCodes.Status404NotFound;
    }
}

[ApiController]
[Route("api/contribute")]
public class ContributionApiController : ControllerBase
{
    private readonly IUserContributionService service;
    private readonly UserManager<TheDiscDbUser> userManager;

    public ContributionApiController(IUserContributionService service, UserManager<TheDiscDbUser> userManager)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetUserContributions(CancellationToken cancellationToken)
    {
        var userId = this.userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return new UserNotFoundResult();
        }

        var result = await this.service.GetUserContributions(userId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("create")]
    [Authorize]
    public async Task<IActionResult> CreateContribution([FromBody] CreateContributionRequest request, CancellationToken cancellationToken)
    {
        var userId = this.userManager.GetUserId(User);
        //var user = await this.userManager.FindByIdAsync(userId!);
        if (string.IsNullOrEmpty(userId))
        {
            return new UserNotFoundResult();
        }

        var result = await this.service.CreateContribution(userId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{contributionId}")]
    [Authorize]
    // Test
    public async Task<ActionResult<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken)
    {
        var result = await this.service.GetContribution(contributionId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{contributionId}")]
    [Authorize]
    // Test
    public async Task<ActionResult> DeleteContribution(string contributionId, CancellationToken cancellationToken)
    {
        var result = await this.service.DeleteContribution(contributionId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{contributionId}")]
    [Authorize]
    // Test
    public async Task<ActionResult> UpdateContribution(string contributionId, [FromBody] CreateContributionRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.UpdateContribution(contributionId, request, cancellationToken);
        return result.ToActionResult();
    }



    [HttpGet("{contributionId}/discs")]
    [Authorize]
    // Test
    public async Task<ActionResult> GetDiscs(string contributionId, CancellationToken cancellationToken)
    {
        var result = await this.service.GetDiscs(contributionId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("{contributionId}/discs/{discId}/logs")]
    public async Task<ActionResult> SaveDiscLogs(string contributionId, string discId, [FromBody] string logs, CancellationToken cancellationToken)
    {
        var result = await this.service.SaveDiscLogs(contributionId, discId, logs, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("{contributionId}/discs/{discId}/logs")]
    //[Authorize]
    public async Task<IActionResult> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken)
    {
        var response = await this.service.GetDiscLogs(contributionId, discId, cancellationToken);
        return response.ToActionResult();
    }

    [HttpPost("{contributionId}/discs/create")]
    [Authorize]
    public async Task<IActionResult> CreateDisc(string contributionId, [FromBody] SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.CreateDisc(contributionId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{contributionId}/discs/{discId}")]
    [Authorize]
    // Test
    public async Task<ActionResult> UpdateDisc(string contributionId, string discId, [FromBody] SaveDiscRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.UpdateDisc(contributionId, discId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{contributionId}/discs/{discId}")]
    [Authorize]
    // Test
    public async Task<ActionResult> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken)
    {
        var result = await this.service.DeleteDisc(contributionId, discId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpGet("checkdiskuploadstatus/{discId}")]
    public async Task<ActionResult> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken)
    {
        var result = await this.service.CheckDiskUploadStatus(discId, cancellationToken);
        return result.ToActionResult();
    }


    [HttpPost("{contributionId}/discs/{discId}/item")]
    [Authorize]
    // Test
    public async Task<IActionResult> AddItemToDisc(string contributionId, string discId, [FromBody] AddItemRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.AddItemToDisc(contributionId, discId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{contributionId}/discs/{discId}/item/{itemId}")]
    [Authorize]
    // Test
    public async Task<ActionResult> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken)
    {
        var result = await this.service.DeleteItemFromDisc(contributionId, discId, itemId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPost("{contributionId}/discs/{discId}/item/{itemId}/chapter")]
    [Authorize]
    // Test
    public async Task<IActionResult> AddChapterToItem(string contributionId, string discId, string itemId, [FromBody] AddChapterRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.AddChapterToItem(contributionId, discId, itemId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{contributionId}/discs/{discId}/item/{itemId}/chapters/{chapterId}")]
    [Authorize]
    // Test
    public async Task<IActionResult> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken)
    {
        var result = await this.service.DeleteChapterFromItem(contributionId, discId, itemId, chapterId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{contributionId}/discs/{discId}/item/{itemId}/chapters/{chapterId}")]
    [Authorize]
    // Test
    public async Task<IActionResult> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, [FromBody] AddChapterRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.UpdateChapterInItem(contributionId, discId, itemId, chapterId, request, cancellationToken);
        return result.ToActionResult();
    }


    [HttpPost("{contributionId}/discs/{discId}/item/{itemId}/audiotrack")]
    [Authorize]
    // Test
    public async Task<IActionResult> AddAudioTrackToItem(string contributionId, string discId, string itemId, [FromBody] AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.AddAudioTrackToItem(contributionId, discId, itemId, request, cancellationToken);
        return result.ToActionResult();
    }

    [HttpDelete("{contributionId}/discs/{discId}/item/{itemId}/audiotracks/{audioTrackId}")]
    [Authorize]
    // Test
    public async Task<IActionResult> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken)
    {
        var result = await this.service.DeleteAudioTrackFromItem(contributionId, discId, itemId, audioTrackId, cancellationToken);
        return result.ToActionResult();
    }

    [HttpPut("{contributionId}/discs/{discId}/item/{itemId}/audiotracks/{audioTrackId}")]
    [Authorize]
    // Test
    public async Task<IActionResult> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, [FromBody] AddAudioTrackRequest request, CancellationToken cancellationToken)
    {
        var result = await this.service.UpdateAudioTrackInItem(contributionId, discId, itemId, audioTrackId, request, cancellationToken);
        return result.ToActionResult();
    }
}