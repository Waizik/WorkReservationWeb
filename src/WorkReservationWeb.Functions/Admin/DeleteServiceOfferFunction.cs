using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class DeleteServiceOfferFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<DeleteServiceOfferFunction>? logger = null)
{
    [Function("AdminDeleteServiceOffer")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/services/{serviceOfferId}")] HttpRequestData request,
        string serviceOfferId,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to delete service offer {ServiceOfferId}.", serviceOfferId);
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        if (string.IsNullOrWhiteSpace(serviceOfferId))
        {
            logger?.LogInformation("Service offer deletion rejected because the id was missing.");
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_service_offer_id", "Service offer id is required."), cancellationToken);
            return badRequest;
        }

        var existingServiceOffer = await reservationPlatformService.GetServiceOfferAsync(serviceOfferId, cancellationToken);
        if (existingServiceOffer is null)
        {
            logger?.LogInformation("Service offer deletion skipped because service offer {ServiceOfferId} was not found.", serviceOfferId);
            var notFound = request.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new ApiErrorDto("service_offer_not_found", "Service offer was not found."), cancellationToken);
            return notFound;
        }

        var deleted = await reservationPlatformService.DeleteServiceOfferAsync(serviceOfferId, cancellationToken);
        if (!deleted)
        {
            logger?.LogInformation("Service offer {ServiceOfferId} could not be deleted because it is still in use.", serviceOfferId);
            var conflict = request.CreateResponse(System.Net.HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new ApiErrorDto("service_offer_in_use", "Service offer cannot be deleted while slots or reservations exist."), cancellationToken);
            return conflict;
        }

        logger?.LogInformation("Service offer {ServiceOfferId} deleted.", serviceOfferId);

        return request.CreateResponse(System.Net.HttpStatusCode.NoContent);
    }
}