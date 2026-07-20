using System.Net.Mail;

namespace WorkReservationWeb.Shared;

public static class EmailAddressValidator
{
    // Syntactic check only: proves the address is well-formed, not that the mailbox exists.
    public static bool IsValid(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // The exact-match guard rejects inputs MailAddress would silently reinterpret,
        // e.g. display-name forms like "User <user@example.com>".
        return MailAddress.TryCreate(trimmed, out var address) &&
               string.Equals(address.Address, trimmed, StringComparison.Ordinal) &&
               address.Host.Contains('.');
    }
}
