using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Public;

public sealed class CreateReservationFunction(
    IReservationPlatformService reservationPlatformService,
    IReservationNotificationService notificationService,
    ILogger<CreateReservationFunction>? logger = null)
{
    [Function("CreateReservation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "public/reservations")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<CreateReservationRequestDto>(cancellationToken);
        if (payload is null)
        {
            logger?.LogWarning("Reservation creation rejected because the request payload was missing.");
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new ApiErrorDto("invalid_payload", "Request payload is required."), cancellationToken);
            return badRequest;
        }

        if (string.IsNullOrWhiteSpace(payload.ServiceOfferId) ||
            string.IsNullOrWhiteSpace(payload.SlotId) ||
            string.IsNullOrWhiteSpace(payload.SlotEtag) ||
            string.IsNullOrWhiteSpace(payload.CustomerName) ||
            string.IsNullOrWhiteSpace(payload.CustomerEmail))
        {
            logger?.LogInformation(
                "Reservation creation validation failed for service offer {ServiceOfferId} and slot {SlotId} because required fields were missing.",
                payload.ServiceOfferId,
                payload.SlotId);
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(
                new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.ValidationFailed,
                    null,
                    "Required fields are missing.",
                    null),
                cancellationToken);
            return badRequest;
        }

        var serviceOffer = await reservationPlatformService.GetServiceOfferAsync(payload.ServiceOfferId, cancellationToken);
        if (serviceOffer is null || !serviceOffer.Active)
        {
            logger?.LogInformation(
                "Reservation creation validation failed because service offer {ServiceOfferId} was unavailable or inactive.",
                payload.ServiceOfferId);
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(
                new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.ValidationFailed,
                    null,
                    "Selected service is not available.",
                    null),
                cancellationToken);
            return badRequest;
        }

        var slot = await reservationPlatformService.GetReservationSlotAsync(payload.ServiceOfferId, payload.SlotId, cancellationToken);
        if (slot is null)
        {
            logger?.LogInformation(
                "Reservation creation validation failed because slot {SlotId} on service offer {ServiceOfferId} did not exist.",
                payload.SlotId,
                payload.ServiceOfferId);
            var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(
                new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.ValidationFailed,
                    null,
                    "Selected slot does not exist.",
                    null),
                cancellationToken);
            return badRequest;
        }

        if (!string.Equals(slot.Etag, payload.SlotEtag, StringComparison.Ordinal))
        {
            logger?.LogInformation(
                "Reservation creation conflicted for service offer {ServiceOfferId} slot {SlotId} because the slot etag changed.",
                payload.ServiceOfferId,
                payload.SlotId);
            var conflict = request.CreateResponse(System.Net.HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(
                new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.Conflict,
                    null,
                    "Slot changed before booking could be completed.",
                    slot.Etag),
                cancellationToken);
            return conflict;
        }

        if (!string.Equals(slot.Status, ReservationSlotStatus.Available, StringComparison.Ordinal) || slot.ReservedCount >= slot.Capacity)
        {
            logger?.LogInformation(
                "Reservation creation conflicted for service offer {ServiceOfferId} slot {SlotId} because the slot was no longer available.",
                payload.ServiceOfferId,
                payload.SlotId);
            var conflict = request.CreateResponse(System.Net.HttpStatusCode.Conflict);
            await conflict.WriteAsJsonAsync(
                new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.Conflict,
                    null,
                    "Slot is no longer available.",
                    slot.Etag),
                cancellationToken);
            return conflict;
        }

        var result = await reservationPlatformService.CreateReservationAsync(payload, cancellationToken);

        logger?.LogInformation(
            "Reservation creation completed with outcome {Outcome} for service offer {ServiceOfferId} slot {SlotId}. ReservationId: {ReservationId}.",
            result.Outcome,
            payload.ServiceOfferId,
            payload.SlotId,
            result.ReservationId);

        if (result.Success && !string.IsNullOrWhiteSpace(result.ReservationId))
        {
            var confirmationContext = new ReservationNotificationContextDto(
                result.ReservationId,
                serviceOffer.Id,
                serviceOffer.Title,
                slot.Id,
                slot.StartUtc,
                slot.EndUtc,
                payload.CustomerName.Trim(),
                payload.CustomerEmail.Trim(),
                payload.Note?.Trim(),
                DateTimeOffset.UtcNow,
                null,
                null);

            try
            {
                var sentAtUtc = DateTimeOffset.UtcNow;
                await notificationService.SendReservationConfirmationAsync(confirmationContext, cancellationToken);
                await reservationPlatformService.MarkReservationConfirmationSentAsync(result.ReservationId, sentAtUtc, cancellationToken);
                logger?.LogInformation("Reservation confirmation notification sent for reservation {ReservationId}.", result.ReservationId);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to send reservation confirmation notification for reservation {ReservationId}.", result.ReservationId);
            }
        }

        var statusCode = result.Outcome switch
        {
            ReservationCreateOutcome.Created => System.Net.HttpStatusCode.Created,
            ReservationCreateOutcome.ValidationFailed => System.Net.HttpStatusCode.BadRequest,
            ReservationCreateOutcome.Conflict => System.Net.HttpStatusCode.Conflict,
            _ => System.Net.HttpStatusCode.BadRequest
        };

        var response = request.CreateResponse(statusCode);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }

    private static class ReservationSlotStatus
    {
        public const string Available = "Available";
    }
}
