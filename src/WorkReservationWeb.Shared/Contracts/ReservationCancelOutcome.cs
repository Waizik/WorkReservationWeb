namespace WorkReservationWeb.Shared.Contracts;

public enum ReservationCancelOutcome
{
    Cancelled = 0,
    NotFound = 1,
    AlreadyCancelled = 2,
    Conflict = 3
}
