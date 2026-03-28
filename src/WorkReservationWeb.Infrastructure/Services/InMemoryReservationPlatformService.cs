using System.Collections.Concurrent;
using WorkReservationWeb.Domain.Entities;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Services;

public sealed class InMemoryReservationPlatformService : IReservationPlatformService
{
    private readonly ConcurrentDictionary<string, ServiceOffer> serviceOffers = new();
    private readonly ConcurrentDictionary<string, ReservationSlot> slots = new();
    private readonly ConcurrentDictionary<string, Reservation> reservations = new();

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
        if (!slots.TryGetValue(slotId, out var slot) || !string.Equals(slot.ServiceOfferId, serviceOfferId, StringComparison.Ordinal))
        {
            return Task.FromResult<ReservationSlotDto?>(null);
        }

        return Task.FromResult<ReservationSlotDto?>(ToDto(slot));
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
        var result = slots
            .Values
            .Where(x => x.ServiceOfferId == serviceOfferId)
            .Where(x => x.StartUtc >= now)
            .Where(x => x.Status == SlotStatus.Available)
            .OrderBy(x => x.StartUtc)
            .Select(ToDto)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ReservationSlotDto>>(result);
    }

    public Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            var slot = slots[request.SlotId];

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
                Status = ReservationStatus.Confirmed
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
                x.Status.ToString()))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ReservationSummaryDto>>(result);
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

        return Task.FromResult(serviceOffers.TryRemove(serviceOfferId, out _));
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

    private static string CreateEtag()
    {
        return Guid.NewGuid().ToString("N");
    }

    private void SeedIfEmpty()
    {
        if (!serviceOffers.IsEmpty || !slots.IsEmpty)
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

        var day = DateTimeOffset.UtcNow.Date.AddDays(1);
        for (var i = 0; i < 4; i++)
        {
            var start = day.AddHours(8 + (i * 2));
            var end = start.AddHours(1);
            var slotId = $"slot_{start:yyyyMMddHHmm}";
            slots[slotId] = new ReservationSlot
            {
                Id = slotId,
                ServiceOfferId = service.Id,
                StartUtc = start,
                EndUtc = end,
                Capacity = 2,
                ReservedCount = 0,
                Status = SlotStatus.Available,
                Etag = CreateEtag()
            };
        }
    }
}
