namespace WorkReservationWeb.Infrastructure.Assets;

public sealed record ServiceOfferImageUpload(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record SavedServiceOfferImage(
    string AssetId,
    string FileName,
    string ContentType,
    long ContentLength);

public sealed record StoredServiceOfferImage(
    string AssetId,
    string FileName,
    string ContentType,
    long ContentLength,
    byte[] Content);