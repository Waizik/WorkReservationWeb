using System.Text.Json;

namespace WorkReservationWeb.Infrastructure.Assets;

public sealed class LocalFileServiceOfferImageStorage : IServiceOfferImageStorage
{
    private readonly string rootPath;

    public LocalFileServiceOfferImageStorage(string rootPath)
    {
        this.rootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public async Task<SavedServiceOfferImage> SaveImageAsync(ServiceOfferImageUpload upload, CancellationToken cancellationToken)
    {
        var assetId = $"img_{Guid.NewGuid():N}";
        var extension = NormalizeExtension(upload.FileName);
        var storedFileName = string.Concat(assetId, extension);
        var contentPath = Path.Combine(rootPath, storedFileName);
        var metadataPath = Path.Combine(rootPath, $"{assetId}.json");

        await File.WriteAllBytesAsync(contentPath, upload.Content, cancellationToken);

        var metadata = new LocalStoredImageMetadata(
            assetId,
            upload.FileName,
            upload.ContentType,
            upload.Content.LongLength,
            storedFileName,
            DateTimeOffset.UtcNow);

        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata), cancellationToken);

        return new SavedServiceOfferImage(assetId, upload.FileName, upload.ContentType, upload.Content.LongLength);
    }

    public async Task<StoredServiceOfferImage?> GetImageAsync(string assetId, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(rootPath, $"{assetId}.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var metadata = JsonSerializer.Deserialize<LocalStoredImageMetadata>(await File.ReadAllTextAsync(metadataPath, cancellationToken));
        if (metadata is null)
        {
            return null;
        }

        var contentPath = Path.Combine(rootPath, metadata.StoredFileName);
        if (!File.Exists(contentPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(contentPath, cancellationToken);
        return new StoredServiceOfferImage(metadata.AssetId, metadata.FileName, metadata.ContentType, metadata.ContentLength, bytes);
    }

    private static string NormalizeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            return string.Empty;
        }

        return extension.All(static ch => char.IsLetterOrDigit(ch) || ch == '.')
            ? extension.ToLowerInvariant()
            : string.Empty;
    }

    private sealed record LocalStoredImageMetadata(
        string AssetId,
        string FileName,
        string ContentType,
        long ContentLength,
        string StoredFileName,
        DateTimeOffset UploadedAtUtc);
}