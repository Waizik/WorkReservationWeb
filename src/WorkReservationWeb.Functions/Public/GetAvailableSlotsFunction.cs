using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Infrastructure.Services;

namespace WorkReservationWeb.Functions.Public;

public sealed class GetAvailableSlotsFunction(IReservationPlatformService reservationPlatformService)
{
    [Function("GetAvailableSlots")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/services/{serviceOfferId}/slots")] HttpRequestData request,
        string serviceOfferId,
        CancellationToken cancellationToken)
    {
        var serviceOffer = await reservationPlatformService.GetServiceOfferAsync(serviceOfferId, cancellationToken);
        if (serviceOffer is null || !serviceOffer.Active)
        {
            var emptyResponse = request.CreateResponse(System.Net.HttpStatusCode.OK);
            await emptyResponse.WriteAsJsonAsync(Array.Empty<object>(), cancellationToken);
            return emptyResponse;
        }

        var result = await reservationPlatformService.GetAvailableSlotsAsync(serviceOfferId, cancellationToken);
        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
