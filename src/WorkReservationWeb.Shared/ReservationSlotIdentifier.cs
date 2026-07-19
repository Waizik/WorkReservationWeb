using System.Globalization;

namespace WorkReservationWeb.Shared;

public static class ReservationSlotIdentifier
{
    private const string Prefix = "slot_";
    private const string TimestampFormat = "yyyyMMddHHmm";

    public static string Create(DateTimeOffset startUtc) =>
        $"{Prefix}{startUtc.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}";

    public static bool TryParseStartUtc(string? slotId, out DateTimeOffset startUtc)
    {
        startUtc = default;
        if (slotId is null || !slotId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                slotId[Prefix.Length..],
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        startUtc = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return true;
    }
}
