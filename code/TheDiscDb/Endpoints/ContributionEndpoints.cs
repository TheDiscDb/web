using Microsoft.AspNetCore.Mvc;
using TheDiscDb.Client;
using TheDiscDb.Data.Import;

namespace TheDiscDb.Web;

public class ContributionEndpoints
{
    public void MapEndpoints(WebApplication app)
    {
        var contribute = app.MapGroup("/api/contribute").RequireAuthorization();

        contribute.MapPost("images/front/upload/{id:guid}", UploadFrontImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("images/front/remove/{id:guid}", RemoveFrontImage)
            .DisableAntiforgery();
        contribute.MapPost("images/back/upload/{id:guid}", UploadBackImage)
            .WithMetadata(new DisableRequestSizeLimitAttribute())
            .DisableAntiforgery();
        contribute.MapPost("images/back/remove/{id:guid}", RemoveBackImage)
            .DisableAntiforgery();
    }

    #region Image Upload

    public async Task<IResult> RemoveFrontImage(Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await RemoveImage(id, "front", service, cancellationToken);

    public async Task<IResult> UploadFrontImage(IFormFileCollection files, Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await UploadImage(files, id, "front", service, cancellationToken);

    public async Task<IResult> RemoveBackImage(Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await RemoveImage(id, "back", service, cancellationToken);

    public async Task<IResult> UploadBackImage(IFormFileCollection files, Guid id, [FromKeyedServices(KeyedServiceNames.ImagesAssetStore)] IStaticAssetStore service, CancellationToken cancellationToken)
        => await UploadImage(files, id, "back", service, cancellationToken);

    public async Task<IResult> RemoveImage(Guid id, string name, IStaticAssetStore service, CancellationToken cancellationToken)
    {
        var path = GetReleaseImagePath(id, name);
        await service.Delete(path, cancellationToken);
        return TypedResults.Ok();
    }

    private async Task<IResult> UploadImage(IFormFileCollection files, Guid id, string name, IStaticAssetStore service, CancellationToken cancellationToken)
    {
        var file = files.FirstOrDefault();

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest("No file uploaded.");
        }

        var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);

        memoryStream.Position = 0;
        string path = GetReleaseImagePath(id, name);
        var result = await service.Save(memoryStream, path, file.ContentType, cancellationToken);

        return TypedResults.Ok();
    }

    private static string GetReleaseImagePath(Guid id, string name) => $"Contributions/releaseImages/{id}/{name}.jpg";

    #endregion
}
