namespace WorkReservationWeb.Shared.Contracts;

public enum ReservationCreateOutcome
{
    Created = 0,
    ValidationFailed = 1,
    Conflict = 2
}
