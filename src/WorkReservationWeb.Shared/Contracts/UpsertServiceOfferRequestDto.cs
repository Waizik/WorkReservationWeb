namespace WorkReservationWeb.Shared.Contracts;

public sealed record UpsertServiceOfferRequestDto(
    string? Id,
    string Title,
    string Description,
    decimal BasePrice,
    IReadOnlyList<string>? ImageUrls,
    bool Active);
