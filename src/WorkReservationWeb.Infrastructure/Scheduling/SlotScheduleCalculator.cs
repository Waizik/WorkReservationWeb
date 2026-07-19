using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Scheduling;

public sealed record ScheduledSlot(string Id, DateTimeOffset StartUtc, DateTimeOffset EndUtc, int Capacity);

public static class SlotScheduleCalculator
{
    private const string SlotIdPrefix = "slot_";
    private const string SlotIdTimestampFormat = "yyyyMMddHHmm";
    public const string OverrideDateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    public static string CreateSlotId(DateTimeOffset startUtc) =>
        $"{SlotIdPrefix}{startUtc.UtcDateTime.ToString(SlotIdTimestampFormat, System.Globalization.CultureInfo.InvariantCulture)}";

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, TimeFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);

    public static bool TryParseOverrideDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, OverrideDateFormat, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

    public static bool IsValidTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ScheduledSlot> GetUpcomingSlots(SlotScheduleDto schedule, DateTimeOffset nowUtc)
    {
        if (schedule.Times.Count == 0 || schedule.SlotDurationMinutes <= 0 || schedule.BookingWindowDays <= 0)
        {
            return [];
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var scheduledDays = schedule.DaysOfWeek.ToHashSet();
        var firstLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);
        var slots = new List<ScheduledSlot>();

        for (var dayOffset = 0; dayOffset < schedule.BookingWindowDays; dayOffset++)
        {
            var localDate = firstLocalDate.AddDays(dayOffset);
            var times = ResolveTimesForDate(schedule, scheduledDays, localDate);

            foreach (var time in times)
            {
                var localStart = localDate.ToDateTime(time, DateTimeKind.Unspecified);
                if (timeZone.IsInvalidTime(localStart))
                {
                    continue;
                }

                var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), TimeSpan.Zero);
                if (startUtc <= nowUtc)
                {
                    continue;
                }

                slots.Add(new ScheduledSlot(
                    CreateSlotId(startUtc),
                    startUtc,
                    startUtc.AddMinutes(schedule.SlotDurationMinutes),
                    schedule.Capacity));
            }
        }

        return slots
            .OrderBy(slot => slot.StartUtc)
            .ToArray();
    }

    public static ScheduledSlot? ResolveSlot(SlotScheduleDto schedule, string slotId, DateTimeOffset nowUtc) =>
        GetUpcomingSlots(schedule, nowUtc).FirstOrDefault(slot => string.Equals(slot.Id, slotId, StringComparison.Ordinal));

    private static IEnumerable<TimeOnly> ResolveTimesForDate(
        SlotScheduleDto schedule,
        HashSet<DayOfWeek> scheduledDays,
        DateOnly localDate)
    {
        var dateKey = localDate.ToString(OverrideDateFormat, System.Globalization.CultureInfo.InvariantCulture);

        IReadOnlyList<string> times;
        if (schedule.Overrides.TryGetValue(dateKey, out var scheduleOverride))
        {
            if (scheduleOverride.Closed)
            {
                return [];
            }

            times = scheduleOverride.Times ?? [];
        }
        else if (scheduledDays.Contains(localDate.DayOfWeek))
        {
            times = schedule.Times;
        }
        else
        {
            return [];
        }

        return times
            .Select(time => TryParseTime(time, out var parsed) ? parsed : (TimeOnly?)null)
            .Where(time => time is not null)
            .Select(time => time!.Value)
            .Distinct()
            .OrderBy(time => time);
    }
}
