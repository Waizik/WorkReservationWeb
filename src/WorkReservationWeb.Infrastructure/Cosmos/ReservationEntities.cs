namespace WorkReservationWeb.Infrastructure.Cosmos;

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

public sealed class ReservationSlot
{
    public required string Id { get; init; }

    public required string ServiceOfferId { get; init; }

    public required DateTimeOffset StartUtc { get; init; }

    public required DateTimeOffset EndUtc { get; init; }

    public int Capacity { get; set; }

    public int ReservedCount { get; set; }

    public SlotStatus Status { get; set; }

    public required string Etag { get; set; }
}

public enum ReservationStatus
{
    Confirmed = 0,
    Cancelled = 1
}

public sealed class ServiceOffer
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public decimal BasePrice { get; set; }

    public List<string> ImageUrls { get; set; } = [];

    public bool Active { get; set; }
}

public enum SlotStatus
{
    Available = 0,
    Full = 1,
    Blocked = 2,
    Cancelled = 3
}