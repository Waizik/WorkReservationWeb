using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class ProcessReservationRemindersFunction(
    ReservationReminderProcessor reminderProcessor,
    ILogger<ProcessReservationRemindersFunction>? logger = null)
{
    [Function("AdminProcessReservationReminders")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/reservations/reminders/process")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to manually process reservation reminders.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        logger?.LogInformation("Manual reservation reminder processing requested.");
        var result = await reminderProcessor.ProcessDueRemindersAsync(cancellationToken);

        logger?.LogInformation(
            "Manual reservation reminder processing completed. Processed {ProcessedCount}, sent {SentCount}, failed {FailedCount}.",
            result.ProcessedCount,
            result.SentCount,
            result.FailedCount);

        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}