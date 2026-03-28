using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkReservationWeb.Infrastructure.Assets;
using WorkReservationWeb.Infrastructure.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "WorkReservationWeb";
var cosmosContainerName = builder.Configuration["CosmosDb:ContainerName"] ?? "Reservations";
var blobStorageConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
var blobStorageContainerName = builder.Configuration["BlobStorage:ContainerName"] ?? "service-offer-images";

if (string.IsNullOrWhiteSpace(cosmosConnectionString))
{
    builder.Services.AddSingleton<IReservationPlatformService, InMemoryReservationPlatformService>();
}
else
{
    builder.Services.AddSingleton<IReservationPlatformService>(_ =>
        new CosmosReservationPlatformService(cosmosConnectionString, cosmosDatabaseName, cosmosContainerName));
}

if (string.IsNullOrWhiteSpace(blobStorageConnectionString))
{
    builder.Services.AddSingleton<IServiceOfferImageStorage>(_ =>
        new LocalFileServiceOfferImageStorage(Path.Combine(AppContext.BaseDirectory, "uploaded-assets")));
}
else
{
    builder.Services.AddSingleton<IServiceOfferImageStorage>(_ =>
        new BlobServiceOfferImageStorage(blobStorageConnectionString, blobStorageContainerName));
}

builder.Build().Run();
