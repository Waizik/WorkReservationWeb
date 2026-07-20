using System.Net;
using Microsoft.Azure.Functions.Worker.Http;

namespace WorkReservationWeb.Functions.Security;

public static class ClientIpResolver
{
    // Azure Static Web Apps / Functions forward the original caller in x-forwarded-for,
    // possibly as a comma-separated chain and with a port suffix.
    public static string Resolve(HttpRequestData request)
    {
        if (!request.Headers.TryGetValues("x-forwarded-for", out var values))
        {
            return "unknown";
        }

        var first = values.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(first))
        {
            return "unknown";
        }

        if (IPEndPoint.TryParse(first, out var endpoint))
        {
            return endpoint.Address.ToString();
        }

        if (IPAddress.TryParse(first, out var address))
        {
            return address.ToString();
        }

        return first;
    }
}
