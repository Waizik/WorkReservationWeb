using Microsoft.Azure.Functions.Worker.Http;

namespace WorkReservationWeb.Functions.Security;

internal static class AdminAuthorization
{
    public static bool IsAuthorized(HttpRequestData request)
    {
        return request.Headers.TryGetValues("x-ms-client-principal", out var values) &&
               values.Any(value => !string.IsNullOrWhiteSpace(value));
    }
}
