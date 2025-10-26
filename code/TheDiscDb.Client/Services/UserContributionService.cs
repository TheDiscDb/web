using System.Net.Http.Json;
using FluentResults;
using Microsoft.AspNetCore.Components;
using TheDiscDb.Client;
using TheDiscDb.Web.Data;

namespace TheDiscDb.Services.Client;

public class UserContributionService : ApiClient, IUserContributionService
{
    public UserContributionService(IHttpClientFactory httpClientFactory, NavigationManager navigation)
        : base(httpClientFactory, navigation)
    {
    }

    #region Contributions

    public async Task<Result<List<UserContribution>>> GetUserContributions(string userId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<List<UserContribution>>("/api/contribute", cancellationToken);

        if (response == null)
        {
            return Result.Fail("Unable to get user contributions from server");
        }

        return response;
    }

    public async Task<Result<CreateContributionResponse>> CreateContribution(string userId, CreateContributionRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync("/api/contribute/create", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateContributionResponse>(cancellationToken: cancellationToken);

        if (result == null)
        {
            return Result.Fail("Unable to get contribution result from server");
        }

        return result;
    }

    public async Task<Result<UserContribution>> GetContribution(string contributionId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<UserContribution>($"/api/contribute/{contributionId}", cancellationToken);

        if (response == null)
        {
            return Result.Fail("Unable to get contribution from server");
        }

        return response;
    }

    public async Task<Result> DeleteContribution(string contributionId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.DeleteAsync($"/api/contribute/{contributionId}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to delete contribution {contributionId} from server");
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateContribution(string contributionId, CreateContributionRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PutAsJsonAsync($"/api/contribute/{contributionId}", request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to update contribution {contributionId} from server");
        }

        return Result.Ok();
    }

    #endregion

    #region Discs

    public async Task<Result<List<UserContributionDisc>>> GetDiscs(string contributionId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<List<UserContributionDisc>>($"/api/contribute/{contributionId}/discs", cancellationToken);

        if (response == null)
        {
            return Result.Fail($"Unable to retrieve discs for {contributionId} from server");
        }

        return Result.Ok(response);
    }

    public async Task<Result> SaveDiscLogs(string contributionId, string discId, string logs, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Result<DiscLogResponse>> GetDiscLogs(string contributionId, string discId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<DiscLogResponse>($"/api/contribute/{contributionId}/discs/{discId}/logs", cancellationToken);

        if (response == null)
        {
            return Result.Fail($"Unable to get disc logs from server for contribution {contributionId}, disc {discId}");
        }

        return response;
    }

    public async Task<Result<SaveDiscResponse>> CreateDisc(string contributionId, SaveDiscRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync($"/api/contribute/{contributionId}/discs/create", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to create disc for contribution {contributionId}");
        }

        var result = await response.Content.ReadFromJsonAsync<SaveDiscResponse>(cancellationToken: cancellationToken);
        return Result.Ok(result!);
    }

    public async Task<Result> UpdateDisc(string contributionId, string discId, SaveDiscRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PutAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to update disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteDisc(string contributionId, string discId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.DeleteAsync($"/api/contribute/{contributionId}/discs/{discId}", cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to delete disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    public async Task<Result<DiscStatusResponse>> CheckDiskUploadStatus(string discId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.GetFromJsonAsync<DiscStatusResponse>($"/api/contribute/checkdiskuploadstatus/{discId}", cancellationToken);

        if (response == null)
        {
            throw new Exception("Unable to get disc status from server");
        }

        return response;
    }

    #endregion

    #region Disc Items

    public async Task<Result<AddItemResponse>> AddItemToDisc(string contributionId, string discId, AddItemRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}/item", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to add item to disc {discId} for contribution {contributionId}");
        }

        var result = await response.Content.ReadFromJsonAsync<AddItemResponse>(cancellationToken: cancellationToken);
        return Result.Ok(result!);
    }

    public async Task<Result> DeleteItemFromDisc(string contributionId, string discId, string itemId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.DeleteAsync($"/api/contribute/{contributionId}/discs/{discId}/item/{itemId}", cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to delete item {itemId} from disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    #endregion

    #region Chapters

    public async Task<Result<AddChapterResponse>> AddChapterToItem(string contributionId, string discId, string itemId, AddChapterRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/chapters", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to add chapter to item {itemId} on disc {discId} for contribution {contributionId}");
        }

        var result = await response.Content.ReadFromJsonAsync<AddChapterResponse>(cancellationToken: cancellationToken);
        return Result.Ok(result!);
    }

    public async Task<Result> DeleteChapterFromItem(string contributionId, string discId, string itemId, string chapterId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.DeleteAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/chapters/{chapterId}", cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to delete chapter {chapterId} from item {itemId} on disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateChapterInItem(string contributionId, string discId, string itemId, string chapterId, AddChapterRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PutAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/chapters/{chapterId}", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to update chapter {chapterId} in item {itemId} on disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    #endregion

    #region Audio Tracks

    public async Task<Result<AddAudioTrackResponse>> AddAudioTrackToItem(string contributionId, string discId, string itemId, AddAudioTrackRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PostAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/audiotracks", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to add audio track to item {itemId} on disc {discId} for contribution {contributionId}");
        }

        var result = await response.Content.ReadFromJsonAsync<AddAudioTrackResponse>(cancellationToken: cancellationToken);
        return Result.Ok(result!);
    }

    public async Task<Result> DeleteAudioTrackFromItem(string contributionId, string discId, string itemId, string audioTrackId, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.DeleteAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/audiotracks/{audioTrackId}", cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to delete audio track {audioTrackId} from item {itemId} on disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateAudioTrackInItem(string contributionId, string discId, string itemId, string audioTrackId, AddAudioTrackRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var response = await client.PutAsJsonAsync($"/api/contribute/{contributionId}/discs/{discId}/items/{itemId}/audiotracks/{audioTrackId}", request, cancellationToken);

        if (response == null || !response.IsSuccessStatusCode)
        {
            return Result.Fail($"Unable to update audio track {audioTrackId} in item {itemId} on disc {discId} for contribution {contributionId}");
        }

        return Result.Ok();
    }

    #endregion
}
