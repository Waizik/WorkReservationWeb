using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Functions.Public;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class EmailValidationTests
{
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("first.last+tag@sub.example.co.uk", true)]
    [InlineData(" user@example.com ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("abc", false)]
    [InlineData("user@", false)]
    [InlineData("@example.com", false)]
    [InlineData("user@localhost", false)]
    [InlineData("User Name <user@example.com>", false)]
    [InlineData("user@example.com, second@example.com", false)]
    public void IsValid_EvaluatesSyntax(string email, bool expected)
    {
        Assert.Equal(expected, EmailAddressValidator.IsValid(email));
    }

    [Fact]
    public async Task CreateReservation_WithMalformedEmail_ReturnsValidationFailed_WithoutCreatingReservation()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CreateReservationFunction(service, new NoopNotificationService());

        var slot = (await service.GetAvailableSlotsAsync("srv_consultation", CancellationToken.None)).First();
        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(
                new CreateReservationRequestDto("srv_consultation", slot.Id, slot.Etag, "User", "not-an-email", null),
                serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<CreateReservationResultDto>(response.Body, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("Customer e-mail address is not valid.", result.Message);
        Assert.Empty(await service.GetReservationsAsync(CancellationToken.None));
    }

    private sealed class NoopNotificationService : IReservationNotificationService
    {
        public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendReservationCancellationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static IServiceProvider CreateServiceProvider(JsonSerializerOptions serializerOptions)
    {
        return new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
    }
}
