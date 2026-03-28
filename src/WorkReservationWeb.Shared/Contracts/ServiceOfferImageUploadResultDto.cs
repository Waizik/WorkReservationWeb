namespace WorkReservationWeb.Shared.Contracts;

public sealed record ServiceOfferImageUploadResultDto(
    string AssetId,
    string Url,
    string FileName,
    string ContentType,
    long ContentLength);