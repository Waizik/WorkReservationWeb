namespace WorkReservationWeb.Domain.Entities;

public sealed class ServiceOffer
{
    public required string Id { get; init; }

    public required string Title { get; set; }

    public required string Description { get; set; }

    public decimal BasePrice { get; set; }

    public List<string> ImageUrls { get; set; } = [];

    public bool Active { get; set; }
}
