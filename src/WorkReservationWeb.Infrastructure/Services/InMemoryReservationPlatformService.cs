using System.Collections.Concurrent;
using WorkReservationWeb.Infrastructure.Cosmos;
using WorkReservationWeb.Infrastructure.Scheduling;
using WorkReservationWeb.Shared;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Services;

public sealed class InMemoryReservationPlatformService : IReservationPlatformService
{
    private readonly ConcurrentDictionary<string, ServiceOffer> serviceOffers = new();
    private readonly ConcurrentDictionary<string, ReservationSlot> slots = new();
    private readonly ConcurrentDictionary<string, Reservation> reservations = new();
    private readonly ConcurrentDictionary<string, SlotScheduleDto> schedules = new();

    private readonly Lock sync = new();

    public InMemoryReservationPlatformService()
    {
        SeedIfEmpty();
    }

    public Task<ServiceOfferDto?> GetServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(serviceOffers.TryGetValue(serviceOfferId, out var serviceOffer)
            ? ToDto(serviceOffer)
            : null);
    }

    public Task<ReservationSlotDto?> GetReservationSlotAsync(string serviceOfferId, string slotId, CancellationToken cancellationToken)
    {
        if (slots.TryGetValue(slotId, out var slot) && string.Equals(slot.ServiceOfferId, serviceOfferId, StringComparison.Ordinal))
        {
            return Task.FromResult<ReservationSlotDto?>(ToDto(slot));
        }

        if (schedules.TryGetValue(serviceOfferId, out var schedule) &&
            SlotScheduleCalculator.ResolveSlot(schedule, slotId, DateTimeOffset.UtcNow) is { } scheduled)
        {
            return Task.FromResult<ReservationSlotDto?>(ToVirtualDto(serviceOfferId, scheduled));
        }

        return Task.FromResult<ReservationSlotDto?>(null);
    }

    public Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken)
    {
        var result = serviceOffers
            .Values
            .OrderBy(x => x.Title)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ServiceOfferDto>>(result);
    }

    public Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken)
    {
        var result = serviceOffers
            .Values
            .Where(x => x.Active)
            .OrderBy(x => x.Title)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ServiceOfferDto>>(result);
    }

    public Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!schedules.TryGetValue(serviceOfferId, out var schedule))
        {
            return Task.FromResult<IReadOnlyList<ReservationSlotDto>>([]);
        }

        var result = SlotScheduleCalculator.GetUpcomingSlots(schedule, now)
            .Select(scheduled =>
            {
                if (slots.TryGetValue(scheduled.Id, out var materialized) &&
                    string.Equals(materialized.ServiceOfferId, serviceOfferId, StringComparison.Ordinal))
                {
                    return materialized.Status == SlotStatus.Available ? ToDto(materialized) : null;
                }

                return ToVirtualDto(serviceOfferId, scheduled);
            })
            .Where(slot => slot is not null)
            .Select(slot => slot!)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ReservationSlotDto>>(result);
    }

    public Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!slots.TryGetValue(request.SlotId, out var slot) ||
                !string.Equals(slot.ServiceOfferId, request.ServiceOfferId, StringComparison.Ordinal))
            {
                if (!schedules.TryGetValue(request.ServiceOfferId, out var schedule) ||
                    SlotScheduleCalculator.ResolveSlot(schedule, request.SlotId, DateTimeOffset.UtcNow) is not { } scheduled)
                {
                    return Task.FromResult(new CreateReservationResultDto(
                        false,
                        ReservationCreateOutcome.ValidationFailed,
                        null,
                        "Selected time slot is not available.",
                        null));
                }

                slot = new ReservationSlot
                {
                    Id = scheduled.Id,
                    ServiceOfferId = request.ServiceOfferId,
                    StartUtc = scheduled.StartUtc,
                    EndUtc = scheduled.EndUtc,
                    Capacity = scheduled.Capacity,
                    ReservedCount = 0,
                    Status = SlotStatus.Available,
                    Etag = string.Empty
                };
                slots[slot.Id] = slot;
            }

            if (!string.Equals(slot.Etag, request.SlotEtag, StringComparison.Ordinal))
            {
                return Task.FromResult(new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.Conflict,
                    null,
                    "Slot changed before booking could be completed.",
                    slot.Etag));
            }

            if (slot.Status != SlotStatus.Available || slot.ReservedCount >= slot.Capacity)
            {
                if (slot.ReservedCount >= slot.Capacity)
                {
                    slot.Status = SlotStatus.Full;
                }

                slot.Etag = CreateEtag();
                slots[slot.Id] = slot;

                return Task.FromResult(new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.Conflict,
                    null,
                    "Slot is no longer available.",
                    slot.Etag));
            }

            var reservationId = $"res_{Guid.NewGuid():N}";
            var reservation = new Reservation
            {
                Id = reservationId,
                ServiceOfferId = slot.ServiceOfferId,
                SlotId = slot.Id,
                CustomerName = request.CustomerName.Trim(),
                CustomerEmail = request.CustomerEmail.Trim(),
                Note = request.Note?.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Status = ReservationStatus.Confirmed,
                ConfirmationSentAtUtc = null,
                ReminderSentAtUtc = null
            };

            reservations[reservationId] = reservation;

            slot.ReservedCount += 1;
            slot.Status = slot.ReservedCount >= slot.Capacity ? SlotStatus.Full : SlotStatus.Available;
            slot.Etag = CreateEtag();
            slots[slot.Id] = slot;

            return Task.FromResult(new CreateReservationResultDto(
                true,
                ReservationCreateOutcome.Created,
                reservationId,
                "Reservation created.",
                slot.Etag));
        }
    }

    public Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken)
    {
        var result = reservations
            .Values
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ReservationSummaryDto(
                x.Id,
                x.ServiceOfferId,
                x.SlotId,
                x.CustomerName,
                x.CustomerEmail,
                x.Note,
                x.CreatedAtUtc,
                x.Status.ToString(),
                x.ConfirmationSentAtUtc,
                x.ReminderSentAtUtc))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ReservationSummaryDto>>(result);
    }

    public Task<CancelReservationResultDto> CancelReservationAsync(string reservationId, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!reservations.TryGetValue(reservationId, out var reservation))
            {
                return Task.FromResult(new CancelReservationResultDto(
                    false,
                    ReservationCancelOutcome.NotFound,
                    "Reservation was not found.",
                    null));
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                return Task.FromResult(new CancelReservationResultDto(
                    false,
                    ReservationCancelOutcome.AlreadyCancelled,
                    "Reservation is already cancelled.",
                    null));
            }

            reservation.Status = ReservationStatus.Cancelled;
            reservations[reservation.Id] = reservation;

            DateTimeOffset slotStartUtc = default;
            DateTimeOffset slotEndUtc = default;
            if (slots.TryGetValue(reservation.SlotId, out var slot))
            {
                slot.ReservedCount = Math.Max(0, slot.ReservedCount - 1);
                if (slot.Status == SlotStatus.Full && slot.ReservedCount < slot.Capacity)
                {
                    slot.Status = SlotStatus.Available;
                }

                slot.Etag = CreateEtag();
                slots[slot.Id] = slot;
                slotStartUtc = slot.StartUtc;
                slotEndUtc = slot.EndUtc;
            }
            else if (ReservationSlotIdentifier.TryParseStartUtc(reservation.SlotId, out var parsedStartUtc))
            {
                slotStartUtc = parsedStartUtc;
                slotEndUtc = parsedStartUtc;
            }

            serviceOffers.TryGetValue(reservation.ServiceOfferId, out var serviceOffer);

            return Task.FromResult(new CancelReservationResultDto(
                true,
                ReservationCancelOutcome.Cancelled,
                "Reservation cancelled.",
                new ReservationNotificationContextDto(
                    reservation.Id,
                    reservation.ServiceOfferId,
                    serviceOffer?.Title ?? reservation.ServiceOfferId,
                    reservation.SlotId,
                    slotStartUtc,
                    slotEndUtc,
                    reservation.CustomerName,
                    reservation.CustomerEmail,
                    reservation.Note,
                    reservation.CreatedAtUtc,
                    reservation.ConfirmationSentAtUtc,
                    reservation.ReminderSentAtUtc)));
        }
    }

    public Task MarkReservationConfirmationSentAsync(string reservationId, DateTimeOffset sentAtUtc, CancellationToken cancellationToken)
    {
        if (reservations.TryGetValue(reservationId, out var reservation))
        {
            reservation.ConfirmationSentAtUtc = sentAtUtc;
            reservations[reservationId] = reservation;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReservationNotificationContextDto>> GetReservationsDueForReminderAsync(DateTimeOffset reminderWindowEndUtc, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var result = reservations.Values
            .Where(reservation => reservation.Status == ReservationStatus.Confirmed)
            .Where(reservation => reservation.ReminderSentAtUtc is null)
            .Select(reservation =>
            {
                slots.TryGetValue(reservation.SlotId, out var slot);
                serviceOffers.TryGetValue(reservation.ServiceOfferId, out var serviceOffer);
                return new { reservation, slot, serviceOffer };
            })
            .Where(candidate => candidate.slot is not null && candidate.serviceOffer is not null)
            .Where(candidate => candidate.slot!.StartUtc >= now && candidate.slot.StartUtc <= reminderWindowEndUtc)
            .OrderBy(candidate => candidate.slot!.StartUtc)
            .Select(candidate => new ReservationNotificationContextDto(
                candidate.reservation.Id,
                candidate.reservation.ServiceOfferId,
                candidate.serviceOffer!.Title,
                candidate.reservation.SlotId,
                candidate.slot!.StartUtc,
                candidate.slot.EndUtc,
                candidate.reservation.CustomerName,
                candidate.reservation.CustomerEmail,
                candidate.reservation.Note,
                candidate.reservation.CreatedAtUtc,
                candidate.reservation.ConfirmationSentAtUtc,
                candidate.reservation.ReminderSentAtUtc))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ReservationNotificationContextDto>>(result);
    }

    public Task MarkReservationReminderSentAsync(string reservationId, DateTimeOffset sentAtUtc, CancellationToken cancellationToken)
    {
        if (reservations.TryGetValue(reservationId, out var reservation))
        {
            reservation.ReminderSentAtUtc = sentAtUtc;
            reservations[reservationId] = reservation;
        }

        return Task.CompletedTask;
    }

    public Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(request.Id)
            ? $"srv_{Guid.NewGuid():N}"
            : request.Id.Trim();

        var normalizedImages = request.ImageUrls?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList() ?? [];

        var offer = new ServiceOffer
        {
            Id = id,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            BasePrice = request.BasePrice,
            Active = request.Active,
            ImageUrls = normalizedImages
        };

        serviceOffers[id] = offer;

        return Task.FromResult(ToDto(offer));
    }

    public Task<bool> DeleteServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        if (slots.Values.Any(slot => string.Equals(slot.ServiceOfferId, serviceOfferId, StringComparison.Ordinal)) ||
            reservations.Values.Any(reservation => string.Equals(reservation.ServiceOfferId, serviceOfferId, StringComparison.Ordinal)))
        {
            return Task.FromResult(false);
        }

        if (!serviceOffers.TryRemove(serviceOfferId, out _))
        {
            return Task.FromResult(false);
        }

        schedules.TryRemove(serviceOfferId, out _);
        return Task.FromResult(true);
    }

    public Task<SlotScheduleDto?> GetSlotScheduleAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        return Task.FromResult(schedules.TryGetValue(serviceOfferId, out var schedule)
            ? schedule
            : null);
    }

    public Task<SlotScheduleDto> UpsertSlotScheduleAsync(SlotScheduleDto schedule, CancellationToken cancellationToken)
    {
        schedules[schedule.ServiceOfferId] = schedule;
        return Task.FromResult(schedule);
    }

    private static ServiceOfferDto ToDto(ServiceOffer offer)
    {
        return new ServiceOfferDto(
            offer.Id,
            offer.Title,
            offer.Description,
            offer.BasePrice,
            offer.ImageUrls,
            offer.Active);
    }

    private static ReservationSlotDto ToDto(ReservationSlot slot)
    {
        return new ReservationSlotDto(
            slot.Id,
            slot.ServiceOfferId,
            slot.StartUtc,
            slot.EndUtc,
            slot.Capacity,
            slot.ReservedCount,
            slot.Status.ToString(),
            slot.Etag);
    }

    private static ReservationSlotDto ToVirtualDto(string serviceOfferId, ScheduledSlot scheduled)
    {
        return new ReservationSlotDto(
            scheduled.Id,
            serviceOfferId,
            scheduled.StartUtc,
            scheduled.EndUtc,
            scheduled.Capacity,
            0,
            SlotStatus.Available.ToString(),
            string.Empty);
    }

    private static string CreateEtag()
    {
        return Guid.NewGuid().ToString("N");
    }

    private void SeedIfEmpty()
    {
        if (!serviceOffers.IsEmpty || !schedules.IsEmpty)
        {
            return;
        }

        var service = new ServiceOffer
        {
            Id = "srv_consultation",
            Title = "Consultation",
            Description = "Initial consultation meeting.",
            BasePrice = 49m,
            Active = true,
            ImageUrls = ["https://example.invalid/images/consultation.jpg"]
        };

        serviceOffers[service.Id] = service;

        schedules[service.Id] = new SlotScheduleDto(
            service.Id,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            ["08:00", "10:00", "12:00", "14:00"],
            SlotDurationMinutes: 60,
            Capacity: 2,
            BookingWindowDays: 28,
            TimeZoneId: "Europe/Prague",
            Overrides: new Dictionary<string, SlotScheduleOverrideDto>());
    }
}
