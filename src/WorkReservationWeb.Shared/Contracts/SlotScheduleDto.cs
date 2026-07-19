namespace WorkReservationWeb.Shared.Contracts;

public sealed record SlotScheduleDto(
    string ServiceOfferId,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    IReadOnlyList<string> Times,
    int SlotDurationMinutes,
    int Capacity,
    int BookingWindowDays,
    string TimeZoneId,
    IReadOnlyDictionary<string, SlotScheduleOverrideDto> Overrides);
