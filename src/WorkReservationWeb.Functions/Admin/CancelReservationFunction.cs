using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class CancelReservationFunction(
    IReservationPlatformService reservationPlatformService,
    IReservationNotificationService notificationService,
    ILogger<CancelReservationFunction>? logger = null)
{
    [Function("AdminCancelReservation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/reservations/{reservationId}/cancel")] HttpRequestData request,
        string reservationId,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to cancel reservation {ReservationId}.", reservationId);
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var result = await reservationPlatformService.CancelReservationAsync(reservationId, cancellationToken);

        logger?.LogInformation(
            "Reservation cancellation completed with outcome {Outcome} for reservation {ReservationId}.",
            result.Outcome,
            reservationId);

        if (result.Success && result.Reservation is not null)
        {
            try
            {
                await notificationService.SendReservationCancellationAsync(result.Reservation, cancellationToken);
                logger?.LogDebug("Reservation cancellation notification sent for reservation {ReservationId}.", reservationId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to send reservation cancellation notification for reservation {ReservationId}.", reservationId);
            }
        }

        var statusCode = result.Outcome switch
        {
            ReservationCancelOutcome.Cancelled => System.Net.HttpStatusCode.OK,
            ReservationCancelOutcome.NotFound => System.Net.HttpStatusCode.NotFound,
            ReservationCancelOutcome.AlreadyCancelled => System.Net.HttpStatusCode.Conflict,
            ReservationCancelOutcome.Conflict => System.Net.HttpStatusCode.Conflict,
            _ => System.Net.HttpStatusCode.BadRequest
        };

        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }
}
