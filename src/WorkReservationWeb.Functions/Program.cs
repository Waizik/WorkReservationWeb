using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkReservationWeb.Infrastructure.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "WorkReservationWeb";
var cosmosContainerName = builder.Configuration["CosmosDb:ContainerName"] ?? "Reservations";

if (string.IsNullOrWhiteSpace(cosmosConnectionString))
{
    builder.Services.AddSingleton<IReservationPlatformService, InMemoryReservationPlatformService>();
}
else
{
    builder.Services.AddSingleton<IReservationPlatformService>(_ =>
        new CosmosReservationPlatformService(cosmosConnectionString, cosmosDatabaseName, cosmosContainerName));
}

builder.Build().Run();
