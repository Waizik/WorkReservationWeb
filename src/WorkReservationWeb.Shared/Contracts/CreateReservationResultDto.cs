namespace WorkReservationWeb.Shared.Contracts;

public sealed record CreateReservationResultDto(
    bool Success,
    ReservationCreateOutcome Outcome,
    string? ReservationId,
    string Message,
    string? UpdatedSlotEtag);
