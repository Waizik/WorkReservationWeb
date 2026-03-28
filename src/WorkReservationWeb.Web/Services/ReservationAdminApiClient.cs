using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Web.Services;

public sealed class ReservationAdminApiClient(HttpClient httpClient, IConfiguration configuration)
{
    private const string ClientPrincipalHeader = "x-ms-client-principal";

    public bool UsesConfiguredClientPrincipal => !string.IsNullOrWhiteSpace(configuration["AdminClientPrincipalHeader"]);

    public async Task<IReadOnlyList<ServiceOfferDto>> GetServiceOffersAsync(CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "api/management/services", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<IReadOnlyList<ServiceOfferDto>>(cancellationToken: cancellationToken);
        return payload ?? [];
    }

    public async Task<IReadOnlyList<ReservationSummaryDto>> GetReservationsAsync(CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "api/management/reservations", cancellationToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<IReadOnlyList<ReservationSummaryDto>>(cancellationToken: cancellationToken);
        return payload ?? [];
    }

    public async Task<ServiceOfferDto?> UpsertServiceOfferAsync(UpsertServiceOfferRequestDto requestDto, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "api/management/services", cancellationToken);
        request.Content = JsonContent.Create(requestDto);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServiceOfferDto>(cancellationToken: cancellationToken);
    }

    private Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = new HttpRequestMessage(method, path);
        var principalHeader = configuration["AdminClientPrincipalHeader"];
        if (!string.IsNullOrWhiteSpace(principalHeader))
        {
            request.Headers.Add(ClientPrincipalHeader, principalHeader);
        }

        return Task.FromResult(request);
    }
}