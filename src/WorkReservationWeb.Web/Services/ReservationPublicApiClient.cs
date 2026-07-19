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

    public async Task<CreateReservationResultDto?> CreateReservationAsync(CreateReservationRequestDto request, string? captchaToken, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/public/reservations")
        {
            Content = JsonContent.Create(request)
        };

        if (!string.IsNullOrWhiteSpace(captchaToken))
        {
            message.Headers.Add("x-captcha-token", captchaToken);
        }

        using var response = await httpClient.SendAsync(message, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CreateReservationResultDto>(cancellationToken: cancellationToken);
    }
}
