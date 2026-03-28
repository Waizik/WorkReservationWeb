using System.Globalization;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Notifications;

internal static class ReservationNotificationContentFactory
{
    public static (string Subject, string Body) CreateConfirmation(ReservationNotificationContextDto reservation)
    {
        return (
            $"Reservation confirmed: {reservation.ServiceTitle}",
            CreateBody("Your reservation is confirmed.", reservation));
    }

    public static (string Subject, string Body) CreateReminder(ReservationNotificationContextDto reservation)
    {
        return (
            $"Reservation reminder: {reservation.ServiceTitle}",
            CreateBody("This is a reminder for your upcoming reservation.", reservation));
    }

    private static string CreateBody(string intro, ReservationNotificationContextDto reservation)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"Hello {reservation.CustomerName},",
            string.Empty,
            intro,
            $"Service: {reservation.ServiceTitle}",
            $"Start: {reservation.SlotStartUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            $"End: {reservation.SlotEndUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}",
            $"Reservation Id: {reservation.ReservationId}",
            string.IsNullOrWhiteSpace(reservation.Note) ? string.Empty : $"Note: {reservation.Note}",
            string.Empty,
            "Thank you."
        }.Where(static line => !string.IsNullOrEmpty(line)));
    }
}