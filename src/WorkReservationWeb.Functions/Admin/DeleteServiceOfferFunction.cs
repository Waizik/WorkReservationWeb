using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class DeleteServiceOfferFunction(IReservationPlatformService reservationPlatformService)
{
    [Function("AdminDeleteServiceOffer")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "management/services/{serviceOfferId}")] HttpRequestData request,
        string serviceOfferId,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        if (string.IsNullOrWhiteSpace(serviceOfferId))
        {
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_service_offer_id", "Service offer id is required."), cancellationToken);
            return badRequest;
        }

        var existingServiceOffer = await reservationPlatformService.GetServiceOfferAsync(serviceOfferId, cancellationToken);
        if (existingServiceOffer is null)
        {
            var notFound = request.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new ApiErrorDto("service_offer_not_found", "Service offer was not found."), cancellationToken);
            return notFound;
        }

        var deleted = await reservationPlatformService.DeleteServiceOfferAsync(serviceOfferId, cancellationToken);
        if (!deleted)
        {
            var conflict = request.CreateResponse(System.Net.HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(new ApiErrorDto("service_offer_in_use", "Service offer cannot be deleted while slots or reservations exist."), cancellationToken);
            return conflict;
        }

        return request.CreateResponse(System.Net.HttpStatusCode.NoContent);
    }
}