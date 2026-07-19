using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Scheduling;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class UpsertSlotScheduleFunction(
    IReservationPlatformService reservationPlatformService,
    ILogger<UpsertSlotScheduleFunction>? logger = null)
{
    [Function("AdminUpsertSlotSchedule")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "management/schedules")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthorization.IsAuthorized(request))
        {
            logger?.LogWarning("Unauthorized attempt to upsert a slot schedule.");
            var unauthorized = request.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
            await unauthorized.WriteAsJsonAsync(new ApiErrorDto("unauthorized", "Admin authentication required."), cancellationToken);
            return unauthorized;
        }

        var payload = await request.ReadFromJsonAsync<SlotScheduleDto>(cancellationToken);
        if (payload is null)
        {
            return await BadRequestAsync(request, "invalid_payload", "Slot schedule payload is required.", cancellationToken);
        }

        var validationError = Validate(payload);
        if (validationError is not null)
        {
            logger?.LogDebug("Slot schedule upsert for service offer {ServiceOfferId} rejected: {Reason}", payload.ServiceOfferId, validationError);
            return await BadRequestAsync(request, "invalid_schedule", validationError, cancellationToken);
        }

        var serviceOffer = await reservationPlatformService.GetServiceOfferAsync(payload.ServiceOfferId, cancellationToken);
        if (serviceOffer is null)
        {
            var notFound = request.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new ApiErrorDto("service_offer_not_found", "Service offer was not found."), cancellationToken);
            return notFound;
        }

        var schedule = await reservationPlatformService.UpsertSlotScheduleAsync(payload, cancellationToken);

        logger?.LogInformation(
            "Slot schedule updated for service offer {ServiceOfferId}: {DayCount} days, {TimeCount} times, {OverrideCount} overrides.",
            schedule.ServiceOfferId,
            schedule.DaysOfWeek.Count,
            schedule.Times.Count,
            schedule.Overrides.Count);

        var response = request.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(schedule, cancellationToken);
        return response;
    }

    private static string? Validate(SlotScheduleDto payload)
    {
        if (string.IsNullOrWhiteSpace(payload.ServiceOfferId))
        {
            return "Service offer id is required.";
        }

        if (payload.DaysOfWeek is null || payload.DaysOfWeek.Count == 0)
        {
            return "Select at least one day of week.";
        }

        if (payload.Times is null || payload.Times.Count == 0)
        {
            return "Add at least one time.";
        }

        if (payload.Times.Any(time => !SlotScheduleCalculator.TryParseTime(time, out _)))
        {
            return "Times must use the HH:mm format.";
        }

        if (payload.SlotDurationMinutes <= 0)
        {
            return "Slot duration must be a positive number of minutes.";
        }

        if (payload.Capacity <= 0)
        {
            return "Capacity must be a positive number.";
        }

        if (payload.BookingWindowDays is <= 0 or > 366)
        {
            return "Booking window must be between 1 and 366 days.";
        }

        if (!SlotScheduleCalculator.IsValidTimeZone(payload.TimeZoneId))
        {
            return "Time zone is not recognized.";
        }

        if (payload.Overrides is null)
        {
            return "Overrides must be provided; use an empty object when there are none.";
        }

        foreach (var (date, scheduleOverride) in payload.Overrides)
        {
            if (!SlotScheduleCalculator.TryParseOverrideDate(date, out _))
            {
                return "Override dates must use the yyyy-MM-dd format.";
            }

            if (!scheduleOverride.Closed &&
                (scheduleOverride.Times ?? []).Any(time => !SlotScheduleCalculator.TryParseTime(time, out _)))
            {
                return "Override times must use the HH:mm format.";
            }
        }

        return null;
    }

    private static async Task<HttpResponseData> BadRequestAsync(HttpRequestData request, string code, string message, CancellationToken cancellationToken)
    {
        var badRequest = request.CreateResponse(System.Net.HttpStatusCode.BadRequest);
        await badRequest.WriteAsJsonAsync(new ApiErrorDto(code, message), cancellationToken);
        return badRequest;
    }
}
