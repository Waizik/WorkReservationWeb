using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WorkReservationWeb.Infrastructure.Assets;

public sealed class BlobServiceOfferImageStorage : IServiceOfferImageStorage
{
    private readonly BlobContainerClient containerClient;

    public BlobServiceOfferImageStorage(string connectionString, string containerName)
    {
        var blobServiceClient = new BlobServiceClient(connectionString);
        containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        containerClient.CreateIfNotExists(PublicAccessType.None);
    }

    public async Task<SavedServiceOfferImage> SaveImageAsync(ServiceOfferImageUpload upload, CancellationToken cancellationToken)
    {
        var assetId = $"img_{Guid.NewGuid():N}";
        var extension = NormalizeExtension(upload.FileName);
        var blobName = string.Concat(assetId, extension);
        var blobClient = containerClient.GetBlobClient(blobName);

        using var stream = new MemoryStream(upload.Content, writable: false);
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = upload.ContentType },
                Metadata = new Dictionary<string, string>
                {
                    ["originalFileName"] = upload.FileName,
                    ["contentLength"] = upload.Content.LongLength.ToString(),
                    ["uploadedAtUtc"] = DateTimeOffset.UtcNow.ToString("O")
                }
            },
            cancellationToken);

        return new SavedServiceOfferImage(assetId, upload.FileName, upload.ContentType, upload.Content.LongLength);
    }

    public async Task<StoredServiceOfferImage?> GetImageAsync(string assetId, CancellationToken cancellationToken)
    {
        await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: assetId, cancellationToken: cancellationToken))
        {
            var blobClient = containerClient.GetBlobClient(blobItem.Name);
            Response<BlobDownloadResult> download = await blobClient.DownloadContentAsync(cancellationToken);
            var metadata = blobItem.Metadata;

            return new StoredServiceOfferImage(
                assetId,
                metadata.TryGetValue("originalFileName", out var fileName) ? fileName : blobItem.Name,
                blobItem.Properties.ContentType ?? "application/octet-stream",
                blobItem.Properties.ContentLength ?? download.Value.Content.ToMemory().Length,
                download.Value.Content.ToArray());
        }

        return null;
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
}