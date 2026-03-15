using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;

namespace WorkReservationWeb.Functions.Security;

internal static class AdminAuthorization
{
    private const string ClientPrincipalHeader = "x-ms-client-principal";

    public static bool IsAuthorized(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues(ClientPrincipalHeader, out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (TryParseClientPrincipal(value, out var principal) &&
                (principal.UserRoles?.Any(role => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)) ?? false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseClientPrincipal(string? encodedValue, out StaticWebAppsClientPrincipal principal)
    {
        principal = StaticWebAppsClientPrincipal.Empty;
        if (string.IsNullOrWhiteSpace(encodedValue))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedValue));
            var parsed = JsonSerializer.Deserialize<StaticWebAppsClientPrincipal>(json);
            if (parsed is null)
            {
                return false;
            }

            principal = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record StaticWebAppsClientPrincipal([property: JsonPropertyName("userRoles")] IReadOnlyList<string>? UserRoles)
    {
        public static StaticWebAppsClientPrincipal Empty { get; } = new([]);
    }
}
