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

public sealed class CreateReservationCaptchaTests
{
    private const string ServiceOfferId = "srv_consultation";

    [Fact]
    public async Task CreateReservation_WithFailingCaptcha_ReturnsValidationFailed()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(service, new NoopNotificationService(), new FakeCaptchaVerifier(false));

        var response = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions, "bad-token"), CancellationToken.None);
        var result = await DeserializeResponseAsync<CreateReservationResultDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(result.Success);
        Assert.Equal(ReservationCreateOutcome.ValidationFailed, result.Outcome);
        Assert.Equal("Security check failed. Please try again.", result.Message);
        Assert.Empty(await service.GetReservationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateReservation_WithPassingCaptcha_CreatesReservation_AndForwardsHeaderToken()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var captchaVerifier = new FakeCaptchaVerifier(true);
        var function = new CreateReservationFunction(service, new NoopNotificationService(), captchaVerifier);

        var response = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions, "good-token"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("good-token", captchaVerifier.LastToken);
        Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateReservation_WithoutConfiguredCaptcha_SkipsVerification()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(service, new NoopNotificationService(), captchaVerifier: null);

        var response = await function.Run(await CreateBookingRequestAsync(service, functionContext, serializerOptions, captchaToken: null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<TestHttpRequestData> CreateBookingRequestAsync(
        InMemoryReservationPlatformService service,
        TestFunctionContext functionContext,
        JsonSerializerOptions serializerOptions,
        string? captchaToken)
    {
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(
                new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", "user@example.com", null),
                serializerOptions));

        if (captchaToken is not null)
        {
            request.Headers.Add("x-captcha-token", captchaToken);
        }

        return request;
    }

    private sealed class FakeCaptchaVerifier(bool verdict) : ICaptchaVerifier
    {
        public string? LastToken { get; private set; }

        public Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken)
        {
            LastToken = token;
            return Task.FromResult(verdict);
        }
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
