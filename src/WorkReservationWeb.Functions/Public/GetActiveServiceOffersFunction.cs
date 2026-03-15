using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Infrastructure.Services;

namespace WorkReservationWeb.Functions.Public;

public sealed class GetActiveServiceOffersFunction(IReservationPlatformService reservationPlatformService)
{
    [Function("GetActiveServiceOffers")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public/services")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var result = await reservationPlatformService.GetActiveServiceOffersAsync(cancellationToken);
        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
