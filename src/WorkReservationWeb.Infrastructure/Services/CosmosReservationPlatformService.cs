using Microsoft.Azure.Cosmos;
using WorkReservationWeb.Domain.Entities;
using WorkReservationWeb.Infrastructure.Cosmos;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Services;

public sealed class CosmosReservationPlatformService : IReservationPlatformService, IAsyncDisposable
{
    private readonly CosmosClient cosmosClient;
    private readonly string databaseName;
    private readonly string containerName;
    private readonly SemaphoreSlim initializationLock = new(1, 1);

    private Container? container;

    public CosmosReservationPlatformService(string connectionString, string databaseName, string containerName)
    {
        cosmosClient = new CosmosClient(connectionString);
        this.databaseName = databaseName;
        this.containerName = containerName;
    }

    public async Task<ServiceOfferDto?> GetServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var serviceOffer = await TryGetServiceOfferAsync(currentContainer, serviceOfferId, cancellationToken);
        return serviceOffer is null
            ? null
            : new ServiceOfferDto(
                serviceOffer.id,
                serviceOffer.Title,
                serviceOffer.Description,
                serviceOffer.BasePrice,
                serviceOffer.ImageUrls,
                serviceOffer.Active);
    }

    public async Task<ReservationSlotDto?> GetReservationSlotAsync(string serviceOfferId, string slotId, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);

        try
        {
            var response = await currentContainer.ReadItemAsync<ReservationSlotDocument>(
                slotId,
                new PartitionKey(serviceOfferId),
                cancellationToken: cancellationToken);

            var slot = response.Resource;
            return new ReservationSlotDto(
                slot.id,
                slot.ServiceOfferId,
                slot.StartUtc,
                slot.EndUtc,
                slot.Capacity,
                slot.ReservedCount,
                slot.Status,
                response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Type = @type")
            .WithParameter("@type", CosmosDocumentTypes.ServiceOffer);

        var results = await ReadAllAsync<ServiceOfferDocument, ServiceOfferDto>(currentContainer, query, document => new ServiceOfferDto(
            document.id,
            document.Title,
            document.Description,
            document.BasePrice,
            document.ImageUrls,
            document.Active), cancellationToken);

        return results.OrderBy(x => x.Title).ToArray();
    }

    public async Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Type = @type AND c.Active = true")
            .WithParameter("@type", CosmosDocumentTypes.ServiceOffer);

        var results = await ReadAllAsync<ServiceOfferDocument, ServiceOfferDto>(currentContainer, query, document => new ServiceOfferDto(
            document.id,
            document.Title,
            document.Description,
            document.BasePrice,
            document.ImageUrls,
            document.Active), cancellationToken);

        return results.OrderBy(x => x.Title).ToArray();
    }

    public async Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Type = @type AND c.ServiceOfferId = @serviceOfferId AND c.Status = @status AND c.StartUtc >= @now")
            .WithParameter("@type", CosmosDocumentTypes.ReservationSlot)
            .WithParameter("@serviceOfferId", serviceOfferId)
            .WithParameter("@status", SlotStatus.Available.ToString())
            .WithParameter("@now", DateTimeOffset.UtcNow);

        var results = await ReadAllAsync<ReservationSlotDocument, ReservationSlotDto>(currentContainer, query, document => new ReservationSlotDto(
            document.id,
            document.ServiceOfferId,
            document.StartUtc,
            document.EndUtc,
            document.Capacity,
            document.ReservedCount,
            document.Status,
            string.Empty), cancellationToken);

        foreach (var slot in results)
        {
            var response = await currentContainer.ReadItemAsync<ReservationSlotDocument>(slot.Id, new PartitionKey(serviceOfferId), cancellationToken: cancellationToken);
            var index = results.FindIndex(candidate => candidate.Id == slot.Id);
            results[index] = slot with { Etag = response.ETag };
        }

        return results.OrderBy(x => x.StartUtc).ToArray();
    }

    public async Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var slotResponse = await currentContainer.ReadItemAsync<ReservationSlotDocument>(
            request.SlotId,
            new PartitionKey(request.ServiceOfferId),
            cancellationToken: cancellationToken);

        var slot = slotResponse.Resource;
        if (!string.Equals(slotResponse.ETag, request.SlotEtag, StringComparison.Ordinal))
        {
            return new CreateReservationResultDto(
                false,
                ReservationCreateOutcome.Conflict,
                null,
                "Slot changed before booking could be completed.",
                slotResponse.ETag);
        }

        if (!string.Equals(slot.Status, SlotStatus.Available.ToString(), StringComparison.Ordinal) || slot.ReservedCount >= slot.Capacity)
        {
            return new CreateReservationResultDto(
                false,
                ReservationCreateOutcome.Conflict,
                null,
                "Slot is no longer available.",
                slotResponse.ETag);
        }

        var updatedReservedCount = slot.ReservedCount + 1;
        var updatedSlot = new ReservationSlotDocument
        {
            id = slot.id,
            partitionKey = slot.partitionKey,
            Type = slot.Type,
            ServiceOfferId = slot.ServiceOfferId,
            StartUtc = slot.StartUtc,
            EndUtc = slot.EndUtc,
            Capacity = slot.Capacity,
            ReservedCount = updatedReservedCount,
            Status = updatedReservedCount >= slot.Capacity ? SlotStatus.Full.ToString() : SlotStatus.Available.ToString()
        };

        var reservationId = $"res_{Guid.NewGuid():N}";
        var reservation = new ReservationDocument
        {
            id = reservationId,
            partitionKey = request.ServiceOfferId,
            Type = CosmosDocumentTypes.Reservation,
            ServiceOfferId = request.ServiceOfferId,
            SlotId = request.SlotId,
            CustomerName = request.CustomerName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim(),
            Note = request.Note?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Status = ReservationStatus.Confirmed.ToString()
        };

        var batch = currentContainer.CreateTransactionalBatch(new PartitionKey(request.ServiceOfferId))
            .ReplaceItem(slot.id, updatedSlot, new TransactionalBatchItemRequestOptions { IfMatchEtag = slotResponse.ETag })
            .CreateItem(reservation);

        using var batchResponse = await batch.ExecuteAsync(cancellationToken);
        if (!batchResponse.IsSuccessStatusCode)
        {
            if (batchResponse.StatusCode == System.Net.HttpStatusCode.PreconditionFailed ||
                batchResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var currentSlot = await currentContainer.ReadItemAsync<ReservationSlotDocument>(slot.id, new PartitionKey(request.ServiceOfferId), cancellationToken: cancellationToken);
                return new CreateReservationResultDto(
                    false,
                    ReservationCreateOutcome.Conflict,
                    null,
                    "Slot changed before booking could be completed.",
                    currentSlot.ETag);
            }

            throw new CosmosException(
                batchResponse.ErrorMessage,
                batchResponse.StatusCode,
                0,
                batchResponse.ActivityId,
                batchResponse.RequestCharge);
        }

        var refreshedSlot = await currentContainer.ReadItemAsync<ReservationSlotDocument>(slot.id, new PartitionKey(request.ServiceOfferId), cancellationToken: cancellationToken);

        return new CreateReservationResultDto(
            true,
            ReservationCreateOutcome.Created,
            reservationId,
            "Reservation created.",
            refreshedSlot.ETag);
    }

    public async Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var query = new QueryDefinition(
            "SELECT * FROM c WHERE c.Type = @type")
            .WithParameter("@type", CosmosDocumentTypes.Reservation);

        var results = await ReadAllAsync<ReservationDocument, ReservationSummaryDto>(currentContainer, query, document => new ReservationSummaryDto(
            document.id,
            document.ServiceOfferId,
            document.SlotId,
            document.CustomerName,
            document.CustomerEmail,
            document.Note,
            document.CreatedAtUtc,
            document.Status), cancellationToken);

        return results.OrderByDescending(x => x.CreatedAtUtc).ToArray();
    }

    public async Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);

        var id = string.IsNullOrWhiteSpace(request.Id)
            ? $"srv_{Guid.NewGuid():N}"
            : request.Id.Trim();

        var normalizedImages = request.ImageUrls?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList() ?? [];
        var document = new ServiceOfferDocument
        {
            id = id,
            partitionKey = id,
            Type = CosmosDocumentTypes.ServiceOffer,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            BasePrice = request.BasePrice,
            ImageUrls = normalizedImages,
            Active = request.Active
        };

        await currentContainer.UpsertItemAsync(document, new PartitionKey(document.partitionKey), cancellationToken: cancellationToken);

        return new ServiceOfferDto(
            document.id,
            document.Title,
            document.Description,
            document.BasePrice,
            document.ImageUrls,
            document.Active);
    }

    public async Task<bool> DeleteServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        var currentContainer = await GetContainerAsync(cancellationToken);
        var linkedDocumentCountQuery = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.partitionKey = @partitionKey AND (c.Type = @slotType OR c.Type = @reservationType)")
            .WithParameter("@partitionKey", serviceOfferId)
            .WithParameter("@slotType", CosmosDocumentTypes.ReservationSlot)
            .WithParameter("@reservationType", CosmosDocumentTypes.Reservation);

        var countIterator = currentContainer.GetItemQueryIterator<int>(linkedDocumentCountQuery);
        var linkedDocumentCount = 0;
        while (countIterator.HasMoreResults)
        {
            var page = await countIterator.ReadNextAsync(cancellationToken);
            linkedDocumentCount += page.FirstOrDefault();
        }

        if (linkedDocumentCount > 0)
        {
            return false;
        }

        try
        {
            await currentContainer.DeleteItemAsync<ServiceOfferDocument>(
                serviceOfferId,
                new PartitionKey(serviceOfferId),
                cancellationToken: cancellationToken);

            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        initializationLock.Dispose();
        cosmosClient.Dispose();
    }

    private async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (container is not null)
        {
            return container;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (container is not null)
            {
                return container;
            }

            var database = await cosmosClient.CreateDatabaseIfNotExistsAsync(databaseName, cancellationToken: cancellationToken);
            var createdContainer = await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(containerName, "/partitionKey"),
                cancellationToken: cancellationToken);

            container = createdContainer.Container;
            return container;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static async Task<ServiceOfferDocument?> TryGetServiceOfferAsync(
        Container currentContainer,
        string serviceOfferId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await currentContainer.ReadItemAsync<ServiceOfferDocument>(
                serviceOfferId,
                new PartitionKey(serviceOfferId),
                cancellationToken: cancellationToken);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private static async Task<List<TDto>> ReadAllAsync<TDocument, TDto>(
        Container currentContainer,
        QueryDefinition query,
        Func<TDocument, TDto> map,
        CancellationToken cancellationToken)
    {
        var iterator = currentContainer.GetItemQueryIterator<TDocument>(query);
        var results = new List<TDto>();

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Select(map));
        }

        return results;
    }
}
