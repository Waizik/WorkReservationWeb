namespace WorkReservationWeb.Domain.Entities;

public sealed class Reservation
{
    public required string Id { get; init; }

    public required string ServiceOfferId { get; init; }

    public required string SlotId { get; init; }

    public required string CustomerName { get; init; }

    public required string CustomerEmail { get; init; }

    public string? Note { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public ReservationStatus Status { get; set; }

    public DateTimeOffset? ConfirmationSentAtUtc { get; set; }

    public DateTimeOffset? ReminderSentAtUtc { get; set; }
}
