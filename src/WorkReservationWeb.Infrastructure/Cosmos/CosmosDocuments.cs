namespace WorkReservationWeb.Infrastructure.Cosmos;

internal static class CosmosDocumentTypes
{
    public const string ServiceOffer = "service-offer";
    public const string ReservationSlot = "reservation-slot";
    public const string Reservation = "reservation";
}

internal sealed class ServiceOfferDocument
{
    public required string id { get; init; }

    public required string partitionKey { get; init; }

    public required string Type { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public decimal BasePrice { get; init; }

    public List<string> ImageUrls { get; init; } = [];

    public bool Active { get; init; }
}

internal sealed class ReservationSlotDocument
{
    public required string id { get; init; }

    public required string partitionKey { get; init; }

    public required string Type { get; init; }

    public required string ServiceOfferId { get; init; }

    public required DateTimeOffset StartUtc { get; init; }

    public required DateTimeOffset EndUtc { get; init; }

    public int Capacity { get; init; }

    public int ReservedCount { get; init; }

    public required string Status { get; init; }
}

internal sealed class ReservationDocument
{
    public required string id { get; init; }

    public required string partitionKey { get; init; }

    public required string Type { get; init; }

    public required string ServiceOfferId { get; init; }

    public required string SlotId { get; init; }

    public required string CustomerName { get; init; }

    public required string CustomerEmail { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required string Status { get; init; }
}
