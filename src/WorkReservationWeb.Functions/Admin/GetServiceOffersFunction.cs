using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class GetServiceOffersFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<GetServiceOffersFunction>? logger = null)
{
    [Function("AdminGetServiceOffers")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/services")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to list service offers.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var result = await reservationPlatformService.GetServiceOffersAsync(cancellationToken);
        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}