namespace WorkReservationWeb.Domain.Entities;

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
