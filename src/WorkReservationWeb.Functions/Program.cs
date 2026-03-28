using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NCrontab;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Infrastructure.Assets;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton<ReservationReminderProcessor>();

var reminderSchedule = builder.Configuration["ReservationReminderSchedule"];
ValidateReminderSchedule(reminderSchedule);

var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "WorkReservationWeb";
var cosmosContainerName = builder.Configuration["CosmosDb:ContainerName"] ?? "Reservations";
var blobStorageConnectionString = builder.Configuration["BlobStorage:ConnectionString"];
var blobStorageContainerName = builder.Configuration["BlobStorage:ContainerName"] ?? "service-offer-images";
var emailConnectionString = builder.Configuration["CommunicationServices:ConnectionString"];
var emailSenderAddress = builder.Configuration["CommunicationServices:SenderAddress"];

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

if (string.IsNullOrWhiteSpace(emailConnectionString) || string.IsNullOrWhiteSpace(emailSenderAddress))
{
    builder.Services.AddSingleton<IReservationNotificationService>(_ =>
        new LocalDevelopmentReservationNotificationService(Path.Combine(AppContext.BaseDirectory, "sent-emails")));
}
else
{
    builder.Services.AddSingleton<IReservationNotificationService>(_ =>
        new AzureCommunicationReservationNotificationService(emailConnectionString, emailSenderAddress));
}

builder.Build().Run();

static void ValidateReminderSchedule(string? reminderSchedule)
{
    if (string.IsNullOrWhiteSpace(reminderSchedule))
    {
        throw new InvalidOperationException(
            "Missing required configuration value 'ReservationReminderSchedule'. Set it in local.settings.json for local runs and in the Azure Function App application settings after deployment.");
    }

    try
    {
        _ = CrontabSchedule.Parse(reminderSchedule, new CrontabSchedule.ParseOptions { IncludingSeconds = true });
    }
    catch (CrontabException ex)
    {
        throw new InvalidOperationException(
            "Configuration value 'ReservationReminderSchedule' must be a valid 6-field NCRONTAB expression including seconds, for example '0 0 0 * * *'.",
            ex);
    }
}
