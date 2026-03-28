using System.Text.Json;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Notifications;

public sealed class LocalDevelopmentReservationNotificationService : IReservationNotificationService
{
    private readonly string rootPath;

    public LocalDevelopmentReservationNotificationService(string rootPath)
    {
        this.rootPath = rootPath;
        Directory.CreateDirectory(rootPath);
    }

    public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
    {
        var content = ReservationNotificationContentFactory.CreateConfirmation(reservation);
        return WriteMessageAsync("confirmation", reservation, content.Subject, content.Body, cancellationToken);
    }

    public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
    {
        var content = ReservationNotificationContentFactory.CreateReminder(reservation);
        return WriteMessageAsync("reminder", reservation, content.Subject, content.Body, cancellationToken);
    }

    private Task WriteMessageAsync(string kind, ReservationNotificationContextDto reservation, string subject, string body, CancellationToken cancellationToken)
    {
        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{kind}_{reservation.ReservationId}.json";
        var payload = new
        {
            kind,
            to = reservation.CustomerEmail,
            subject,
            body,
            reservation.ReservationId,
            reservation.ServiceTitle,
            reservation.SlotStartUtc,
            reservation.SlotEndUtc,
            generatedAtUtc = DateTimeOffset.UtcNow
        };

        return File.WriteAllTextAsync(Path.Combine(rootPath, fileName), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}