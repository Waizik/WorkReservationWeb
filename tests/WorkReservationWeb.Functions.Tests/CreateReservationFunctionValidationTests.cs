using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using WorkReservationWeb.Functions.Public;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public class CreateReservationFunctionValidationTests
{
    [Fact]
    public async Task Run_WithMissingRequiredFields_ReturnsBadRequestWithoutCallingService()
    {
        var service = new RecordingReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        var function = new CreateReservationFunction(service);

        var requestPayload = new CreateReservationRequestDto(
            string.Empty,
            "slot-1",
            "etag-1",
            "",
            "user@example.com",
            null);

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(requestPayload, serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var result = await DeserializeResponseAsync<CreateReservationResultDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(result.Success);
        Assert.Equal(ReservationCreateOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Required fields are missing.", result.Message);
        Assert.Equal(0, service.CreateReservationCallCount);
    }

    private static async Task<T> DeserializeResponseAsync<T>(HttpResponseData response, JsonSerializerOptions serializerOptions)
    {
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(response.Body, serializerOptions);
        return Assert.IsType<T>(result);
    }

    private sealed class RecordingReservationPlatformService : IReservationPlatformService
    {
        public int CreateReservationCallCount { get; private set; }

        public Task<ServiceOfferDto?> GetServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ServiceOfferDto?>(new ServiceOfferDto("srv-1", "Service", "Description", 10m, [], true));
        }

        public Task<ReservationSlotDto?> GetReservationSlotAsync(string serviceOfferId, string slotId, CancellationToken cancellationToken)
        {
            return Task.FromResult<ReservationSlotDto?>(new ReservationSlotDto("slot-1", serviceOfferId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), 2, 0, "Available", "etag-1"));
        }

        public Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
        {
            CreateReservationCallCount++;
            return Task.FromResult(new CreateReservationResultDto(true, ReservationCreateOutcome.Created, "res-1", "Reservation created.", "etag-2"));
        }

        public Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}