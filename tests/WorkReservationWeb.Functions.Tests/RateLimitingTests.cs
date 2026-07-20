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

public sealed class RateLimitingTests
{
    private const string ServiceOfferId = "srv_consultation";

    [Fact]
    public void FixedWindowRateLimiter_AllowsUpToLimit_ThenBlocks()
    {
        var limiter = new FixedWindowRateLimiter(limit: 3, window: TimeSpan.FromHours(1));

        Assert.True(limiter.TryAcquire("ip-1"));
        Assert.True(limiter.TryAcquire("ip-1"));
        Assert.True(limiter.TryAcquire("ip-1"));
        Assert.False(limiter.TryAcquire("ip-1"));
    }

    [Fact]
    public void FixedWindowRateLimiter_TracksClientsIndependently()
    {
        var limiter = new FixedWindowRateLimiter(limit: 1, window: TimeSpan.FromHours(1));

        Assert.True(limiter.TryAcquire("ip-1"));
        Assert.False(limiter.TryAcquire("ip-1"));
        Assert.True(limiter.TryAcquire("ip-2"));
    }

    [Fact]
    public void ClientIpResolver_ReadsFirstForwardedAddress_AndStripsPort()
    {
        var functionContext = new TestFunctionContext(CreateServiceProvider(CreateSerializerOptions()));
        var request = new TestHttpRequestData(functionContext, "POST", new Uri("https://localhost/api/public/reservations"));
        request.Headers.Add("x-forwarded-for", "203.0.113.7:54321, 10.0.0.1");

        Assert.Equal("203.0.113.7", ClientIpResolver.Resolve(request));
    }

    [Fact]
    public void ClientIpResolver_WithoutForwardedHeader_ReturnsUnknown()
    {
        var functionContext = new TestFunctionContext(CreateServiceProvider(CreateSerializerOptions()));
        var request = new TestHttpRequestData(functionContext, "POST", new Uri("https://localhost/api/public/reservations"));

        Assert.Equal("unknown", ClientIpResolver.Resolve(request));
    }

    [Fact]
    public async Task CreateReservation_OverRateLimit_ReturnsTooManyRequests_AndCreatesNoReservation()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(
            service,
            new NoopNotificationService(),
            captchaVerifier: null,
            rateLimiter: new FixedWindowRateLimiter(limit: 1, window: TimeSpan.FromHours(1)));

        var first = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions), CancellationToken.None);
        var result = await DeserializeResponseAsync<CreateReservationResultDto>(second, serializerOptions);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.False(result.Success);
        Assert.Equal("Too many booking attempts. Please try again later.", result.Message);
        Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateReservation_WithoutConfiguredRateLimiter_IsUnlimited()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(service, new NoopNotificationService());

        var first = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions), CancellationToken.None);
        var second = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    private static async Task<TestHttpRequestData> CreateBookingRequestAsync(
        InMemoryReservationPlatformService service,
        TestFunctionContext functionContext,
        JsonSerializerOptions serializerOptions)
    {
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(
                new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", $"user-{Guid.NewGuid():N}@example.com", null),
                serializerOptions));
        request.Headers.Add("x-forwarded-for", "203.0.113.7");
        return request;
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
