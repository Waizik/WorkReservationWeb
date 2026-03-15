using System.Net.Http.Json;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Web.Services;

public sealed class ReservationPublicApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ServiceOfferDto>> GetServicesAsync(CancellationToken cancellationToken)
    {
        var payload = await httpClient.GetFromJsonAsync<IReadOnlyList<ServiceOfferDto>>("api/public/services", cancellationToken);
        return payload ?? [];
    }

    public async Task<IReadOnlyList<ReservationSlotDto>> GetSlotsAsync(string serviceOfferId, CancellationToken cancellationToken)
    {
        var payload = await httpClient.GetFromJsonAsync<IReadOnlyList<ReservationSlotDto>>($"api/public/services/{serviceOfferId}/slots", cancellationToken);
        return payload ?? [];
    }

    public async Task<CreateReservationResultDto?> CreateReservationAsync(CreateReservationRequestDto request, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("api/public/reservations", request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CreateReservationResultDto>(cancellationToken: cancellationToken);
    }
}
