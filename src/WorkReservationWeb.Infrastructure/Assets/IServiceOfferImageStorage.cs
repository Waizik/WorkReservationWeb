namespace WorkReservationWeb.Infrastructure.Assets;

public interface IServiceOfferImageStorage
{
    Task<SavedServiceOfferImage> SaveImageAsync(ServiceOfferImageUpload upload, CancellationToken cancellationToken);

    Task<StoredServiceOfferImage?> GetImageAsync(string assetId, CancellationToken cancellationToken);
}