using Microsoft.Azure.Cosmos;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Integration.Tests;

public sealed class CosmosReservationPlatformServiceTests
{
    [Fact]
    public async Task CreateReservationAsync_UsesTransactionalBatch_WhenCosmosTestsAreEnabled()
    {
        var settings = CosmosTestSettings.TryFromEnvironment();
        if (settings is null)
        {
            return;
        }

        await using var harness = await CosmosTestHarness.CreateAsync(settings, CancellationToken.None);

        await harness.SeedAsync(CancellationToken.None);

        await using var service = new CosmosReservationPlatformService(
            settings.ConnectionString,
            harness.DatabaseName,
            settings.ContainerName);

        var serviceOffers = await service.GetActiveServiceOffersAsync(CancellationToken.None);
        var serviceOffer = Assert.Single(serviceOffers);

        var availableSlots = await service.GetAvailableSlotsAsync(serviceOffer.Id, CancellationToken.None);
        var slot = Assert.Single(availableSlots);

        var result = await service.CreateReservationAsync(
            new CreateReservationRequestDto(
                serviceOffer.Id,
                slot.Id,
                slot.Etag,
                "Cosmos Integration",
                "cosmos.integration@example.com",
                "Transactional booking validation"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ReservationCreateOutcome.Created, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.ReservationId));
        Assert.False(string.IsNullOrWhiteSpace(result.UpdatedSlotEtag));

        var updatedSlot = await service.GetReservationSlotAsync(serviceOffer.Id, slot.Id, CancellationToken.None);

        Assert.NotNull(updatedSlot);
        Assert.Equal(1, updatedSlot.ReservedCount);
        Assert.Equal("Full", updatedSlot.Status);
        Assert.Equal(result.UpdatedSlotEtag, updatedSlot.Etag);

        var reservations = await service.GetReservationsAsync(CancellationToken.None);
        var reservation = Assert.Single(reservations);

        Assert.Equal(result.ReservationId, reservation.Id);
        Assert.Equal(serviceOffer.Id, reservation.ServiceOfferId);
        Assert.Equal(slot.Id, reservation.SlotId);
        Assert.Equal("Cosmos Integration", reservation.CustomerName);
        Assert.Equal("cosmos.integration@example.com", reservation.CustomerEmail);
        Assert.Equal("Confirmed", reservation.Status);

        var remainingSlots = await service.GetAvailableSlotsAsync(serviceOffer.Id, CancellationToken.None);
        Assert.Empty(remainingSlots);
    }

    private sealed record CosmosTestSettings(string ConnectionString, string DatabasePrefix, string ContainerName)
    {
        private const string EnabledVariable = "WORKRESERVATION_RUN_COSMOS_TESTS";
        private const string ConnectionStringVariable = "WORKRESERVATION_COSMOS_TEST_CONNECTION_STRING";
        private const string DatabaseVariable = "WORKRESERVATION_COSMOS_TEST_DATABASE";
        private const string ContainerVariable = "WORKRESERVATION_COSMOS_TEST_CONTAINER";

        public static CosmosTestSettings? TryFromEnvironment()
        {
            var enabled = Environment.GetEnvironmentVariable(EnabledVariable);
            if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Environment variable '{ConnectionStringVariable}' is required when {EnabledVariable}=true.");
            }

            var databasePrefix = Environment.GetEnvironmentVariable(DatabaseVariable);
            if (string.IsNullOrWhiteSpace(databasePrefix))
            {
                databasePrefix = "WorkReservationWebIntegrationTests";
            }

            var containerName = Environment.GetEnvironmentVariable(ContainerVariable);
            if (string.IsNullOrWhiteSpace(containerName))
            {
                containerName = "Reservations";
            }

            return new CosmosTestSettings(connectionString, databasePrefix, containerName);
        }
    }

    private sealed class CosmosTestHarness : IAsyncDisposable
    {
        private readonly CosmosClient client;
        private readonly Database database;

        private CosmosTestHarness(CosmosClient client, Database database, string containerName)
        {
            this.client = client;
            this.database = database;
            ContainerName = containerName;
        }

        public string DatabaseName => database.Id;

        public string ContainerName { get; }

        public static async Task<CosmosTestHarness> CreateAsync(CosmosTestSettings settings, CancellationToken cancellationToken)
        {
            var client = new CosmosClient(settings.ConnectionString);
            var databaseName = $"{settings.DatabasePrefix}-{Guid.NewGuid():N}";
            var databaseResponse = await client.CreateDatabaseIfNotExistsAsync(databaseName, cancellationToken: cancellationToken);
            await databaseResponse.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(settings.ContainerName, "/partitionKey"),
                cancellationToken: cancellationToken);

            return new CosmosTestHarness(client, databaseResponse.Database, settings.ContainerName);
        }

        public async Task SeedAsync(CancellationToken cancellationToken)
        {
            var container = database.GetContainer(ContainerName);
            var serviceOfferId = "srv_cosmos_test";

            await container.CreateItemAsync(
                new ServiceOfferSeedDocument
                {
                    id = serviceOfferId,
                    partitionKey = serviceOfferId,
                    Type = "service-offer",
                    Title = "Cosmos Test Offer",
                    Description = "Offer used for opt-in Cosmos integration coverage.",
                    BasePrice = 99m,
                    ImageUrls = [],
                    Active = true
                },
                new PartitionKey(serviceOfferId),
                cancellationToken: cancellationToken);

            await container.CreateItemAsync(
                new ReservationSlotSeedDocument
                {
                    id = "slot_cosmos_test",
                    partitionKey = serviceOfferId,
                    Type = "reservation-slot",
                    ServiceOfferId = serviceOfferId,
                    StartUtc = DateTimeOffset.UtcNow.AddHours(2),
                    EndUtc = DateTimeOffset.UtcNow.AddHours(3),
                    Capacity = 1,
                    ReservedCount = 0,
                    Status = "Available"
                },
                new PartitionKey(serviceOfferId),
                cancellationToken: cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await database.DeleteAsync();
            client.Dispose();
        }
    }

    private sealed class ServiceOfferSeedDocument
    {
        public required string id { get; init; }

        public required string partitionKey { get; init; }

        public required string Type { get; init; }

        public required string Title { get; init; }

        public required string Description { get; init; }

        public decimal BasePrice { get; init; }

        public List<string> ImageUrls { get; init; } = [];

        public bool Active { get; init; }
    }

    private sealed class ReservationSlotSeedDocument
    {
        public required string id { get; init; }

        public required string partitionKey { get; init; }

        public required string Type { get; init; }

        public required string ServiceOfferId { get; init; }

        public required DateTimeOffset StartUtc { get; init; }

        public required DateTimeOffset EndUtc { get; init; }

        public int Capacity { get; init; }

        public int ReservedCount { get; init; }

        public required string Status { get; init; }
    }
}