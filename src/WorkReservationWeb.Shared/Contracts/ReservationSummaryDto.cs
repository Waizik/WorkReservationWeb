namespace WorkReservationWeb.Shared.Contracts;

public sealed record ReservationSummaryDto(
    string Id,
    string ServiceOfferId,
    string SlotId,
    string CustomerName,
    string CustomerEmail,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    string Status);
