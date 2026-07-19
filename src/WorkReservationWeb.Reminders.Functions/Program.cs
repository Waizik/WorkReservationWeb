using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NCrontab;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Infrastructure.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// The Application Insights SDK registers a default filter rule that only lets Warning+ logs
// through to Application Insights; remove it so Information logs are exported as well.
builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    var applicationInsightsRule = options.Rules.FirstOrDefault(rule =>
        rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (applicationInsightsRule is not null)
    {
        options.Rules.Remove(applicationInsightsRule);
    }
});

if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
}

builder.Services.AddSingleton<ReservationReminderProcessor>();

var reminderSchedule = builder.Configuration["ReservationReminderSchedule"];
ValidateReminderSchedule(reminderSchedule);

var cosmosConnectionString = builder.Configuration["CosmosDb:ConnectionString"];
var cosmosDatabaseName = builder.Configuration["CosmosDb:DatabaseName"] ?? "WorkReservationWeb";
var cosmosContainerName = builder.Configuration["CosmosDb:ContainerName"] ?? "Reservations";
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

if (string.IsNullOrWhiteSpace(emailConnectionString) || string.IsNullOrWhiteSpace(emailSenderAddress))
{
    builder.Services.AddSingleton<IReservationNotificationService>(_ =>
        new LocalDevelopmentReservationNotificationService(
            // Local fallback data must live outside AppContext.BaseDirectory: the Functions host watches the
            // script root for changes and writing there triggers a host restart that breaks in-flight state.
            Path.Combine(Path.GetTempPath(), "WorkReservationWeb", "sent-emails")));
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
