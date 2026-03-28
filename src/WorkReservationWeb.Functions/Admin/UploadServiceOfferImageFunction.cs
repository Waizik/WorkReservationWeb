using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Assets;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class UploadServiceOfferImageFunction(IServiceOfferImageStorage imageStorage)
{
    [Function("AdminUploadServiceOfferImage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/service-images")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
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
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "File name, content type, and content are required."), cancellationToken);
            return badRequest;
        }

        if (!payload.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
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
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Image content must be base64 encoded."), cancellationToken);
            return badRequest;
        }

        if (content.Length == 0 || content.Length > 5 * 1024 * 1024)
        {
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

        var response = request.CreateResponse(System.Net.HttpStatusCode.Created);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}