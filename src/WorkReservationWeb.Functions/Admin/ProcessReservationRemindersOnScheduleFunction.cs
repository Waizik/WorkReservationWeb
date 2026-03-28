using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Microsoft.Extensions.Logging;

namespace WorkReservationWeb.Functions.Admin;

public sealed class ProcessReservationRemindersOnScheduleFunction(
    ReservationReminderProcessor reminderProcessor,
    ILogger<ProcessReservationRemindersOnScheduleFunction> logger)
{
    [Function("ProcessReservationRemindersOnSchedule")]
    public async Task Run(
        [TimerTrigger("%ReservationReminderSchedule%")]
        TimerInfo? timerInfo,
        CancellationToken cancellationToken)
    {
        var result = await reminderProcessor.ProcessDueRemindersAsync(cancellationToken);

        logger.LogInformation(
            "Scheduled reminder run completed at {RunTimeUtc}. Next run: {NextRunUtc}. Processed {ProcessedCount}, sent {SentCount}, failed {FailedCount}.",
            DateTimeOffset.UtcNow,
            timerInfo?.ScheduleStatus?.Next,
            result.ProcessedCount,
            result.SentCount,
            result.FailedCount);
    }
}