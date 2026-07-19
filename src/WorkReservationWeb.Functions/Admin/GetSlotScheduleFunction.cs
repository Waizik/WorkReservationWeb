using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class GetSlotScheduleFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<GetSlotScheduleFunction>? logger = null)
{
    [Function("AdminGetSlotSchedule")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "management/services/{serviceOfferId}/schedule")] HttpRequestData request,
        string serviceOfferId,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to read the slot schedule for service offer {ServiceOfferId}.", serviceOfferId);
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var schedule = await reservationPlatformService.GetSlotScheduleAsync(serviceOfferId, cancellationToken);
        if (schedule is null)
        {
            var notFound = request.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new ApiErrorDto("slot_schedule_not_found", "No slot schedule is defined for this service offer."), cancellationToken);
            return notFound;
        }

        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(schedule, cancellationToken);
        return response;
    }
}
