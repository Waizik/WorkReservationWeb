using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using WorkReservationWeb.Web;
using WorkReservationWeb.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
var httpBaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
	? new Uri(builder.HostEnvironment.BaseAddress)
	: new Uri(apiBaseUrl, UriKind.Absolute);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = httpBaseAddress });
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, StaticWebAppsAuthenticationStateProvider>();
builder.Services.AddScoped<ReservationPublicApiClient>();
builder.Services.AddScoped<ReservationAdminApiClient>();

await builder.Build().RunAsync();
