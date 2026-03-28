using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Assets;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class UploadServiceOfferImageFunction(
    IServiceOfferImageStorage imageStorage,
    ILogger<UploadServiceOfferImageFunction>? logger = null)
{
    [Function("AdminUploadServiceOfferImage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/service-images")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to upload a service offer image.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var payload = await request.ReadFromJsonAsync<UploadServiceOfferImageRequestDto>(cancellationToken);
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.FileName) ||
            string.IsNullOrWhiteSpace(payload.ContentType) ||
            string.IsNullOrWhiteSpace(payload.ContentBase64))
        {
            logger?.LogInformation("Service offer image upload rejected because required fields were missing.");
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "File name, content type, and content are required."), cancellationToken);
            return badRequest;
        }

        if (!payload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogInformation("Service offer image upload rejected for file {FileName} because content type {ContentType} was not an image.", payload.FileName, payload.ContentType);
            var unsupported = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await unsupported.WriteAsJsonAsync(new ApiErrorDto("invalid_content_type", "Only image uploads are supported."), cancellationToken);
            return unsupported;
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(payload.ContentBase64);
        }
        catch (FormatException)
        {
            logger?.LogInformation("Service offer image upload rejected for file {FileName} because the content was not valid base64.", payload.FileName);
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Image content must be base64 encoded."), cancellationToken);
            return badRequest;
        }

        if (content.Length == 0 || content.Length > 5 * 1024 * 1024)
        {
            logger?.LogInformation("Service offer image upload rejected for file {FileName} because size {ContentLength} bytes was outside the allowed range.", payload.FileName, content.Length);
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Image size must be between 1 byte and 5 MB."), cancellationToken);
            return badRequest;
        }

        var savedImage = await imageStorage.SaveImageAsync(
            new ServiceOfferImageUpload(payload.FileName.Trim(), payload.ContentType.Trim(), content),
            cancellationToken);

        var baseUri = request.Url.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        var result = new ServiceOfferImageUploadResultDto(
            savedImage.AssetId,
            $"{baseUri}/api/public/assets/{savedImage.AssetId}",
            savedImage.FileName,
            savedImage.ContentType,
            savedImage.ContentLength);

        logger?.LogInformation(
            "Service offer image {AssetId} uploaded for file {FileName} with content type {ContentType} and size {ContentLength} bytes.",
            result.AssetId,
            result.FileName,
            result.ContentType,
            result.ContentLength);

        var response = request.CreateResponse(System.Net.HttpStatusCode.Created);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}