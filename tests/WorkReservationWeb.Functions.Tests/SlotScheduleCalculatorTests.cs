using WorkReservationWeb.Infrastructure.Scheduling;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class SlotScheduleCalculatorTests
{
    private const string ServiceOfferId = "srv-1";
    private const string PragueTimeZone = "Europe/Prague";

    [Fact]
    public void GetUpcomingSlots_ReturnsSlotsOnlyForScheduledDaysWithinWindow()
    {
        // Monday 2026-07-20 00:00 UTC; schedule covers Mondays and Wednesdays for one week.
        var nowUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule(
            [DayOfWeek.Monday, DayOfWeek.Wednesday],
            ["10:00", "08:00"],
            bookingWindowDays: 7);

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        // Two days in the window (Mon 20th, Wed 22nd) with two times each.
        Assert.Equal(4, slots.Count);
        Assert.All(slots, slot => Assert.True(slot.StartUtc > nowUtc));
        Assert.Equal(slots.OrderBy(slot => slot.StartUtc).Select(slot => slot.Id), slots.Select(slot => slot.Id));
        // 08:00 Prague summer time is 06:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero), slots[0].StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 7, 0, 0, TimeSpan.Zero), slots[0].EndUtc);
        Assert.Equal("slot_202607200600", slots[0].Id);
        Assert.Equal(2, slots[0].Capacity);
    }

    [Fact]
    public void GetUpcomingSlots_SkipsPastTimesOfCurrentDay()
    {
        // Monday 2026-07-20 09:00 UTC = 11:00 in Prague; only the 14:00 slot remains that day.
        var nowUtc = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule([DayOfWeek.Monday], ["08:00", "14:00"], bookingWindowDays: 1);

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        var slot = Assert.Single(slots);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero), slot.StartUtc);
    }

    [Fact]
    public void GetUpcomingSlots_ClosedOverrideRemovesDay()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule(
            [DayOfWeek.Monday, DayOfWeek.Wednesday],
            ["08:00"],
            bookingWindowDays: 7,
            overrides: new Dictionary<string, SlotScheduleOverrideDto>
            {
                ["2026-07-22"] = new(true, null)
            });

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        var slot = Assert.Single(slots);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 6, 0, 0, TimeSpan.Zero), slot.StartUtc);
    }

    [Fact]
    public void GetUpcomingSlots_OverrideReplacesTimesForSpecificDate_EvenOnUnscheduledDay()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule(
            [DayOfWeek.Monday],
            ["08:00"],
            bookingWindowDays: 7,
            overrides: new Dictionary<string, SlotScheduleOverrideDto>
            {
                ["2026-07-20"] = new(false, ["16:00"]),
                ["2026-07-25"] = new(false, ["09:00"])
            });

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        Assert.Equal(2, slots.Count);
        // Monday keeps only the override time, Saturday (normally unscheduled) gains one.
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 14, 0, 0, TimeSpan.Zero), slots[0].StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 25, 7, 0, 0, TimeSpan.Zero), slots[1].StartUtc);
    }

    [Fact]
    public void GetUpcomingSlots_SkipsInvalidLocalTimeDuringSpringForward()
    {
        // In Prague 2027-03-28 02:30 does not exist (clocks jump 02:00 -> 03:00).
        var nowUtc = new DateTimeOffset(2027, 3, 26, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule([DayOfWeek.Sunday], ["02:30", "10:00"], bookingWindowDays: 4);

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        var slot = Assert.Single(slots);
        // 10:00 local after the switch is UTC+2.
        Assert.Equal(new DateTimeOffset(2027, 3, 28, 8, 0, 0, TimeSpan.Zero), slot.StartUtc);
    }

    [Fact]
    public void GetUpcomingSlots_HandlesAmbiguousLocalTimeDuringFallBack()
    {
        // In Prague 2027-10-31 02:30 occurs twice; the slot must still be produced exactly once.
        var nowUtc = new DateTimeOffset(2027, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule([DayOfWeek.Sunday], ["02:30"], bookingWindowDays: 4);

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        Assert.Single(slots);
    }

    [Fact]
    public void ResolveSlot_FindsSlotByGeneratedId_AndRejectsUnknownIds()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule([DayOfWeek.Monday], ["08:00"], bookingWindowDays: 7);
        var expected = Assert.Single(SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc));

        var resolved = SlotScheduleCalculator.ResolveSlot(schedule, expected.Id, nowUtc);
        var unknown = SlotScheduleCalculator.ResolveSlot(schedule, "slot_202607210800", nowUtc);

        Assert.Equal(expected, resolved);
        Assert.Null(unknown);
    }

    [Fact]
    public void GetUpcomingSlots_ToleratesDuplicateAndUnsortedTimes()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var schedule = CreateSchedule([DayOfWeek.Monday], ["14:00", "08:00", "14:00", "not-a-time"], bookingWindowDays: 1);

        var slots = SlotScheduleCalculator.GetUpcomingSlots(schedule, nowUtc);

        Assert.Equal(2, slots.Count);
        Assert.True(slots[0].StartUtc < slots[1].StartUtc);
    }

    private static SlotScheduleDto CreateSchedule(
        IReadOnlyList<DayOfWeek> days,
        IReadOnlyList<string> times,
        int bookingWindowDays,
        IReadOnlyDictionary<string, SlotScheduleOverrideDto>? overrides = null)
    {
        return new SlotScheduleDto(
            ServiceOfferId,
            days,
            times,
            SlotDurationMinutes: 60,
            Capacity: 2,
            BookingWindowDays: bookingWindowDays,
            TimeZoneId: PragueTimeZone,
            Overrides: overrides ?? new Dictionary<string, SlotScheduleOverrideDto>());
    }
}
