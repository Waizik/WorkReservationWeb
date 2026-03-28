namespace WorkReservationWeb.Shared.Contracts;

public sealed record UploadServiceOfferImageRequestDto(
    string FileName,
    string ContentType,
    string ContentBase64);