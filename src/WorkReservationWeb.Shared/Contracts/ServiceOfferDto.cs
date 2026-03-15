namespace WorkReservationWeb.Shared.Contracts;

public sealed record ServiceOfferDto(
    string Id,
    string Title,
    string Description,
    decimal BasePrice,
    IReadOnlyList<string> ImageUrls,
    bool Active);
