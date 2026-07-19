using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class CancelReservationTests
{
    private const string ServiceOfferId = "srv_consultation";

    [Fact]
    public async Task CancelReservation_ReleasesSlotCapacity_AndMarksReservationCancelled()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var booking = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", "user@example.com", null),
            CancellationToken.None);
        Assert.True(booking.Success);

        var result = await service.CancelReservationAsync(booking.ReservationId!, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(ReservationCancelOutcome.Cancelled, result.Outcome);
        Assert.NotNull(result.Reservation);
        Assert.Equal("user@example.com", result.Reservation.CustomerEmail);
        Assert.Equal(slot.StartUtc, result.Reservation.SlotStartUtc);

        var reservation = Assert.Single(await service.GetReservationsAsync(CancellationToken.None));
        Assert.Equal("Cancelled", reservation.Status);

        var releasedSlot = await service.GetReservationSlotAsync(ServiceOfferId, slot.Id, CancellationToken.None);
        Assert.NotNull(releasedSlot);
        Assert.Equal(0, releasedSlot.ReservedCount);
        Assert.Equal("Available", releasedSlot.Status);
    }

    [Fact]
    public async Task CancelReservation_OnFullSlot_MakesSlotBookableAgain()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();

        var first = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "First", "first@example.com", null),
            CancellationToken.None);
        var second = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, first.UpdatedSlotEtag!, "Second", "second@example.com", null),
            CancellationToken.None);
        Assert.True(second.Success);

        // Seeded capacity is 2, so the slot is now Full and no longer offered.
        Assert.DoesNotContain(
            await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None),
            candidate => candidate.Id == slot.Id);

        var result = await service.CancelReservationAsync(first.ReservationId!, CancellationToken.None);
        Assert.True(result.Success);

        var reopenedSlot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None))
            .FirstOrDefault(candidate => candidate.Id == slot.Id);
        Assert.NotNull(reopenedSlot);
        Assert.Equal(1, reopenedSlot.ReservedCount);
    }

    [Fact]
    public async Task CancelReservation_Twice_ReturnsAlreadyCancelled()
    {
        var service = new InMemoryReservationPlatformService();
        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var booking = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", "user@example.com", null),
            CancellationToken.None);

        var first = await service.CancelReservationAsync(booking.ReservationId!, CancellationToken.None);
        var second = await service.CancelReservationAsync(booking.ReservationId!, CancellationToken.None);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(ReservationCancelOutcome.AlreadyCancelled, second.Outcome);
    }

    [Fact]
    public async Task CancelReservation_UnknownId_ReturnsNotFound()
    {
        var service = new InMemoryReservationPlatformService();

        var result = await service.CancelReservationAsync("res_missing", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ReservationCancelOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task CancelReservationFunction_WithoutAdminRole_ReturnsUnauthorized()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CancelReservationFunction(service, new RecordingNotificationService());

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/reservations/res_x/cancel"));

        var response = await function.Run(request, "res_x", CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task CancelReservationFunction_CancelsReservation_AndSendsCancellationNotification()
    {
        var service = new InMemoryReservationPlatformService();
        var notificationService = new RecordingNotificationService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new CancelReservationFunction(service, notificationService);

        var slot = (await service.GetAvailableSlotsAsync(ServiceOfferId, CancellationToken.None)).First();
        var booking = await service.CreateReservationAsync(
            new CreateReservationRequestDto(ServiceOfferId, slot.Id, slot.Etag, "User", "user@example.com", null),
            CancellationToken.None);

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri($"https://localhost/api/management/reservations/{booking.ReservationId}/cancel"));
        request.Headers.Add("x-ms-client-principal", CreateClientPrincipalHeaderValue("authenticated", "admin"));

        var response = await function.Run(request, booking.ReservationId!, CancellationToken.None);
        var result = await DeserializeResponseAsync<CancelReservationResultDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.Success);
        Assert.Equal(ReservationCancelOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, notificationService.CancellationCallCount);
        Assert.Equal("user@example.com", notificationService.LastCancelledReservation?.CustomerEmail);
    }

    private sealed class RecordingNotificationService : IReservationNotificationService
    {
        public int CancellationCallCount { get; private set; }

        public ReservationNotificationContextDto? LastCancelledReservation { get; private set; }

        public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SendReservationCancellationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
        {
            CancellationCallCount++;
            LastCancelledReservation = reservation;
            return Task.CompletedTask;
        }
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

    private static string CreateClientPrincipalHeaderValue(params string[] roles)
    {
        var principal = new
        {
            identityProvider = "aad",
            userId = "test-admin-id",
            userDetails = "admin@example.com",
            userRoles = roles
        };

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal)));
    }
}
