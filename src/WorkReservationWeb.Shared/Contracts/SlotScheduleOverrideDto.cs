namespace WorkReservationWeb.Shared.Contracts;

public sealed record SlotScheduleOverrideDto(
    bool Closed,
    IReadOnlyList<string>? Times);
