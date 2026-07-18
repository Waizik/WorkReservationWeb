using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class GetReservationsFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<GetReservationsFunction>? logger = null)
{
    [Function("AdminGetReservations")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/reservations")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to list reservations.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var result = await reservationPlatformService.GetReservationsAsync(cancellationToken);
        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
