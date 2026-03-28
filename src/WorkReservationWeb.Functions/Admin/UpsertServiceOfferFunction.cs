using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class UpsertServiceOfferFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<UpsertServiceOfferFunction>? logger = null)
{
    [Function("AdminUpsertServiceOffer")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/services")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to upsert a service offer.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var payload = await request.ReadFromJsonAsync<UpsertServiceOfferRequestDto>(cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Description))
        {
            logger?.LogInformation("Service offer upsert rejected because title or description was missing.");
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Title and description are required."), cancellationToken);
            return badRequest;
        }

        var result = await reservationPlatformService.UpsertServiceOfferAsync(payload, cancellationToken);
        logger?.LogInformation(
            "Service offer {ServiceOfferId} saved. Active: {Active}. ImageCount: {ImageCount}.",
            result.Id,
            result.Active,
            result.ImageUrls.Count);
        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
