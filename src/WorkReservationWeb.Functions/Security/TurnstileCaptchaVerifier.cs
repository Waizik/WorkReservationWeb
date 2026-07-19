using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace WorkReservationWeb.Functions.Security;

public sealed class TurnstileCaptchaVerifier(HttpClient httpClient, string secretKey) : ICaptchaVerifier
{
    private static readonly Uri VerifyUri = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"] = secretKey,
            ["response"] = token
        });

        using var response = await httpClient.PostAsync(VerifyUri, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await response.Content.ReadFromJsonAsync<TurnstileVerifyResponse>(cancellationToken: cancellationToken);
        return payload?.Success == true;
    }

    private sealed record TurnstileVerifyResponse([property: JsonPropertyName("success")] bool Success);
}
