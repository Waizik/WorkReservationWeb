using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace WorkReservationWeb.Web.Services;

public sealed class StaticWebAppsAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private readonly IConfiguration configuration;
    private readonly IWebAssemblyHostEnvironment hostEnvironment;
    private readonly Lazy<Task<AuthenticationState>> authenticationState;

    public StaticWebAppsAuthenticationStateProvider(
        IConfiguration configuration,
        IWebAssemblyHostEnvironment hostEnvironment)
    {
        this.configuration = configuration;
        this.hostEnvironment = hostEnvironment;
        authenticationState = new Lazy<Task<AuthenticationState>>(() => LoadAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => authenticationState.Value;

    private async Task<AuthenticationState> LoadAuthenticationStateAsync()
    {
        var configuredPrincipal = TryParsePrincipal(configuration["AdminClientPrincipalHeader"]);
        if (configuredPrincipal is not null)
        {
            return CreateAuthenticationState(configuredPrincipal);
        }

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(hostEnvironment.BaseAddress) };
            var response = await httpClient.GetAsync(".auth/me");
            if (!response.IsSuccessStatusCode)
            {
                return AnonymousState;
            }

            var authPayload = await response.Content.ReadFromJsonAsync<StaticWebAppsAuthResponse>();
            return authPayload?.ClientPrincipal is null
                ? AnonymousState
                : CreateAuthenticationState(authPayload.ClientPrincipal);
        }
        catch
        {
            return AnonymousState;
        }
    }

    private static AuthenticationState CreateAuthenticationState(StaticWebAppsPrincipal principal)
    {
        var roles = principal.UserRoles?.Where(role => !string.IsNullOrWhiteSpace(role)).ToArray() ?? [];
        var isAuthenticated = roles.Any(role => !string.Equals(role, "anonymous", StringComparison.OrdinalIgnoreCase));
        if (!isAuthenticated)
        {
            return AnonymousState;
        }

        var claims = new List<Claim>();
        if (!string.IsNullOrWhiteSpace(principal.UserId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, principal.UserId));
        }

        if (!string.IsNullOrWhiteSpace(principal.UserDetails))
        {
            claims.Add(new Claim(ClaimTypes.Name, principal.UserDetails));
        }

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "StaticWebApps");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private static StaticWebAppsPrincipal? TryParsePrincipal(string? encodedPrincipal)
    {
        if (string.IsNullOrWhiteSpace(encodedPrincipal))
        {
            return null;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPrincipal));
            return JsonSerializer.Deserialize<StaticWebAppsPrincipal>(json);
        }
        catch
        {
            return null;
        }
    }

    private sealed record StaticWebAppsAuthResponse(
        [property: JsonPropertyName("clientPrincipal")] StaticWebAppsPrincipal? ClientPrincipal);

    private sealed record StaticWebAppsPrincipal(
        [property: JsonPropertyName("identityProvider")] string? IdentityProvider,
        [property: JsonPropertyName("userId")] string? UserId,
        [property: JsonPropertyName("userDetails")] string? UserDetails,
        [property: JsonPropertyName("userRoles")] IReadOnlyList<string>? UserRoles);
}