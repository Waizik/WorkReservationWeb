using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Infrastructure.Assets;

namespace WorkReservationWeb.Functions.Public;

public sealed class GetServiceOfferImageFunction(IServiceOfferImageStorage imageStorage)
{
    [Function("GetServiceOfferImage")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/assets/{assetId}")] HttpRequestData request,
        string assetId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return request.CreateResponse(System.Net.HttpStatusCode.NotFound);
        }

        var asset = await imageStorage.GetImageAsync(assetId, cancellationToken);
        if (asset is null)
        {
            return request.CreateResponse(System.Net.HttpStatusCode.NotFound);
        }

        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", asset.ContentType);
        response.Headers.Add("Cache-Control", "public, max-age=86400");
        response.Headers.Add("Content-Length", asset.ContentLength.ToString());
        await response.Body.WriteAsync(asset.Content, cancellationToken);
        response.Body.Position = 0;
        return response;
    }
}