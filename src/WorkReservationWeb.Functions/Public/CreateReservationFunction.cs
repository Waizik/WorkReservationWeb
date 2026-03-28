using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Public;

public sealed class CreateReservationFunction(IReservationPlatformService reservationPlatformService)
{
    [Function("CreateReservation")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "public/reservations")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var payload = await request.ReadFromJsonAsync<CreateReservationRequestDto>(cancellationToken);
        if (payload is null)
        {
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
