namespace WorkReservationWeb.Shared.Contracts;

public sealed record CancelReservationResultDto(
    bool Success,
    ReservationCancelOutcome Outcome,
    string Message,
    ReservationNotificationContextDto? Reservation);
