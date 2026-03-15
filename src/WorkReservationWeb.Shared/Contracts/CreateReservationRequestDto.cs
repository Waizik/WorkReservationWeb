namespace WorkReservationWeb.Shared.Contracts;

public sealed record CreateReservationRequestDto(
    string ServiceOfferId,
    string SlotId,
    string SlotEtag,
    string CustomerName,
    string CustomerEmail,
    string? Note);
