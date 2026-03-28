using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class ProcessReservationRemindersFunctionTests
{
    [Fact]
    public async Task Run_SendsDueReminders_AndMarksReservations()
    {
        var service = new RecordingReservationPlatformService();
        var notifications = new RecordingReservationNotificationService();
        var processor = CreateProcessor(service, notifications);
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        var function = new ProcessReservationRemindersFunction(processor);

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/reservations/reminders/process"));
        request.Headers.Add("x-ms-client-principal", CreateClientPrincipalHeaderValue("authenticated", "admin"));

        var response = await function.Run(request, CancellationToken.None);
        var result = await DeserializeResponseAsync<ProcessReservationRemindersResultDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, notifications.ReminderCallCount);
        Assert.Equal("res-1", service.MarkedReminderReservationId);
    }

    [Fact]
    public async Task RunOnSchedule_SendsDueReminders_AndMarksReservations()
    {
        var service = new RecordingReservationPlatformService();
        var notifications = new RecordingReservationNotificationService();
        var processor = CreateProcessor(service, notifications);
        var function = new ProcessReservationRemindersOnScheduleFunction(processor, NullLogger<ProcessReservationRemindersOnScheduleFunction>.Instance);

        await function.Run(null, CancellationToken.None);

        Assert.Equal(1, notifications.ReminderCallCount);
        Assert.Equal("res-1", service.MarkedReminderReservationId);
    }

    private static ReservationReminderProcessor CreateProcessor(
        RecordingReservationPlatformService service,
        RecordingReservationNotificationService notifications)
    {
        return new ReservationReminderProcessor(service, notifications, NullLogger<ReservationReminderProcessor>.Instance);
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

    private sealed class RecordingReservationPlatformService : IReservationPlatformService
    {
        public Task<ServiceOfferDto?> GetServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ReservationSlotDto?> GetReservationSlotAsync(string serviceOfferId, string slotId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceOfferDto>> GetActiveServiceOffersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReservationSlotDto>> GetAvailableSlotsAsync(string serviceOfferId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CreateReservationResultDto> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkReservationConfirmationSentAsync(string reservationId, DateTimeOffset sentAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReservationNotificationContextDto>> GetReservationsDueForReminderAsync(DateTimeOffset reminderWindowEndUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReservationNotificationContextDto>>([
                new ReservationNotificationContextDto(
                    "res-1",
                    "srv-1",
                    "Consultation",
                    "slot-1",
                    DateTimeOffset.UtcNow.AddHours(4),
                    DateTimeOffset.UtcNow.AddHours(5),
                    "User",
                    "user@example.com",
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    null)
            ]);
        }

        public Task MarkReservationReminderSentAsync(string reservationId, DateTimeOffset sentAtUtc, CancellationToken cancellationToken)
        {
            MarkedReminderReservationId = reservationId;
            return Task.CompletedTask;
        }

        public Task<ServiceOfferDto> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteServiceOfferAsync(string serviceOfferId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public string? MarkedReminderReservationId { get; private set; }
    }

    private sealed class RecordingReservationNotificationService : IReservationNotificationService
    {
        public int ReminderCallCount { get; private set; }

        public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
        {
            ReminderCallCount++;
            return Task.CompletedTask;
        }
    }
}