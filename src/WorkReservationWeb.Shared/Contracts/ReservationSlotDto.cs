namespace WorkReservationWeb.Shared.Contracts;

public sealed record ReservationSlotDto(
    string Id,
    string ServiceOfferId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int Capacity,
    int ReservedCount,
    string Status,
    string Etag);
