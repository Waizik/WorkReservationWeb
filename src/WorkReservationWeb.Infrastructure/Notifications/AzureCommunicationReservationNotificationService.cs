using Azure;
using Azure.Communication.Email;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Infrastructure.Notifications;

public sealed class AzureCommunicationReservationNotificationService : IReservationNotificationService
{
    private readonly EmailClient emailClient;
    private readonly string senderAddress;

    public AzureCommunicationReservationNotificationService(string connectionString, string senderAddress)
    {
        emailClient = new EmailClient(connectionString);
        this.senderAddress = senderAddress;
    }

    public Task SendReservationConfirmationAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
    {
        var content = ReservationNotificationContentFactory.CreateConfirmation(reservation);
        return SendAsync(reservation.CustomerEmail, content.Subject, content.Body, cancellationToken);
    }

    public Task SendReservationReminderAsync(ReservationNotificationContextDto reservation, CancellationToken cancellationToken)
    {
        var content = ReservationNotificationContentFactory.CreateReminder(reservation);
        return SendAsync(reservation.CustomerEmail, content.Subject, content.Body, cancellationToken);
    }

    private async Task SendAsync(string recipientAddress, string subject, string body, CancellationToken cancellationToken)
    {
        var content = new EmailContent(subject)
        {
            PlainText = body
        };

        var recipients = new EmailRecipients([new EmailAddress(recipientAddress)]);
        var message = new EmailMessage(senderAddress, recipients, content);
        await emailClient.SendAsync(WaitUntil.Completed, message, cancellationToken);
    }
}