namespace WorkReservationWeb.Shared.Contracts;

public sealed record ProcessReservationRemindersResultDto(
    int ProcessedCount,
    int SentCount,
    int FailedCount,
    DateTimeOffset ReminderWindowEndUtc);