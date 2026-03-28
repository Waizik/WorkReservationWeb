using Microsoft.Extensions.Logging;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Admin;

public sealed class ReservationReminderProcessor(
    IReservationPlatformService reservationPlatformService,
    IReservationNotificationService notificationService,
    ILogger<ReservationReminderProcessor> logger)
{
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(24);

    public async Task<ProcessReservationRemindersResultDto> ProcessDueRemindersAsync(CancellationToken cancellationToken)
    {
        var reminderWindowEndUtc = DateTimeOffset.UtcNow.Add(ReminderWindow);
        var dueReservations = await reservationPlatformService.GetReservationsDueForReminderAsync(reminderWindowEndUtc, cancellationToken);

        var sentCount = 0;
        var failedCount = 0;
        foreach (var reservation in dueReservations)
        {
            try
            {
                var sentAtUtc = DateTimeOffset.UtcNow;
                await notificationService.SendReservationReminderAsync(reservation, cancellationToken);
                await reservationPlatformService.MarkReservationReminderSentAsync(reservation.ReservationId, sentAtUtc, cancellationToken);
                sentCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                logger.LogWarning(ex, "Failed to send reminder for reservation {ReservationId}.", reservation.ReservationId);
            }
        }

        logger.LogInformation(
            "Processed {ProcessedCount} reminder candidates. Sent {SentCount}, failed {FailedCount}.",
            dueReservations.Count,
            sentCount,
            failedCount);

        return new ProcessReservationRemindersResultDto(dueReservations.Count, sentCount, failedCount, reminderWindowEndUtc);
    }
}