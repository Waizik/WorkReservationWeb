using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Notifications;

public interface IReservationNotificationService
{
    Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken);

    Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken);
}