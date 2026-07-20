using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Functions.Public;
using WorkReservationWeb.Functions.Security;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class OpenReservationLimitTests
{
    private const string ServiceOfferId = "srv_consultation";

    [Fact]
    public async Task CountOpenReservations_CountsOnlyConfirmedUpcomingReservations_CaseInsensitively()
    {
        var service = new InMemoryReservationPlatformService();
        var slots = await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None);

        var first = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slots[0].Id, slots[0].Etag, "User", "User@Example.com", null),
            CancellationToken.None);
        var second = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slots[1].Id, slots[1].Etag, "User", "user@example.com", null),
            CancellationToken.None);
        Assert.True(first.Success);
        Assert.True(second.Success);

        Assert.Equal(2, await service.CountOpenReservationsAsync("USER@EXAMPLE.COM", DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(0, await service.CountOpenReservationsAsync("someone.else@example.com", DateTimeOffset.UtcNow, CancellationToken.None));

        // A cancelled reservation stops counting as open.
        await service.CancelReservationAsync(first.ReservationId!, CancellationToken.None);
        Assert.Equal(1, await service.CountOpenReservationsAsync("user@example.com", DateTimeOffset.UtcNow, CancellationToken.None));

        // A reservation whose slot already started is not open either.
        Assert.Equal(0, await service.CountOpenReservationsAsync("user@example.com", DateTimeOffset.UtcNow.AddDays(40), CancellationToken.None));
    }

    [Fact]
    public async Task CreateReservation_OverOpenReservationLimit_ReturnsValidationFailed()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(
            service,
            new NoopNotificationService(),
            bookingLimits: new BookingLimitOptions(MaxOpenReservationsPerEmail: 1));

        var first = await function.Run(
            await CreateBookingRequestAsync(service, functionContext, serializerOptions, slotIndex: 0, "user@example.com"),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await function.Run(
            await CreateBookingRequestAsync(service, functionContext, serializerOptions, slotIndex: 1, "USER@example.com"),
            CancellationToken.None);
        var result = await DeserializeResponseAsync<CreateReservationResultDto>(second, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.False(result.Success);
        Assert.Equal("You already have the maximum number of upcoming reservations for this e-mail address.", result.Message);

        var otherCustomer = await function.Run(
            await CreateBookingRequestAsync(service, functionContext, serializerOptions, slotIndex: 1, "someone.else@example.com"),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, otherCustomer.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_AfterCancellation_AllowsBookingAgain()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(
            service,
            new NoopNotificationService(),
            bookingLimits: new BookingLimitOptions(MaxOpenReservationsPerEmail: 1));

        var first = await function.Run(
            await CreateBookingRequestAsync(service, functionContext, serializerOptions, slotIndex: 0, "user@example.com"),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var reservation = Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
        await service.CancelReservationAsync(reservation.Id, CancellationToken.None);

        var second = await function.Run(
            await CreateBookingRequestAsync(service, functionContext, serializerOptions, slotIndex: 1, "user@example.com"),
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    private static async Task<TestHttpRequestData> CreateBookingRequestAsync(
        InMemoryReservationPlatformService service,
        TestFunctionContext functionContext,
        JsonSerializerOptions serializerOptions,
        int slotIndex,
        string customerEmail)
    {
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None))[slotIndex];
        return new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(
                new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", customerEmail, null),
                serializerOptions));
    }

    private sealed class NoopNotificationService : IReservationNotificationService
    {
        public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendReservationCancellationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static JsonSerializerOptions CreateSerializerOptions() => new(JsonSerializerDefaults.Web);

    private static IServiceProvider CreateServiceProvider(JsonSerializerOptions serializerOptions)
    {
        return new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
    }

    private static async Task<T> DeserializeResponseAsync<T>(HttpResponseData response, JsonSerializerOptions serializerOptions)
    {
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(response.Body, serializerOptions);
        return Assert.IsType<T>(result);
    }
}
