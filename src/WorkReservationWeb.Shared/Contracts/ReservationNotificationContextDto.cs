namespace WorkReservationWeb.Shared.Contracts;

public sealed record ReservationNotificationContextDto(
    string ReservationId,
    string ServiceOfferId,
    string ServiceTitle,
    string SlotId,
    DateTimeOffset SlotStartUtc,
    DateTimeOffset SlotEndUtc,
    string CustomerName,
    string CustomerEmail,
    string? Note,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ConfirmationSentAtUtc,
    DateTimeOffset? ReminderSentAtUtc);