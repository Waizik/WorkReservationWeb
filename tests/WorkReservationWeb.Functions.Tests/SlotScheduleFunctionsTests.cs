using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker.Http;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class SlotScheduleFunctionsTests
{
    [Fact]
    public async Task UpsertSchedule_WithoutAdminRole_ReturnsUnauthorized()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new UpsertSlotScheduleFunction(service);

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/schedules"),
            JsonSerializer.Serialize(CreateValidSchedule(), serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task UpsertSchedule_WithoutDays_ReturnsBadRequest()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new UpsertSlotScheduleFunction(service);

        var request = CreateAdminRequest(
            functionContext,
            "POST",
            "https://localhost/api/management/schedules",
            JsonSerializer.Serialize(CreateValidSchedule() with { DaysOfWeek = [] }, serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_schedule", error.Code);
    }

    [Fact]
    public async Task UpsertSchedule_WithInvalidOverrideDateKey_ReturnsBadRequest()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new UpsertSlotScheduleFunction(service);

        var request = CreateAdminRequest(
            functionContext,
            "POST",
            "https://localhost/api/management/schedules",
            JsonSerializer.Serialize(
                CreateValidSchedule() with
                {
                    Overrides = new Dictionary<string, SlotScheduleOverrideDto>
                    {
                        ["not-a-date"] = new(true, null)
                    }
                },
                serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_schedule", error.Code);
    }

    [Fact]
    public async Task UpsertSchedule_ForUnknownServiceOffer_ReturnsNotFound()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var function = new UpsertSlotScheduleFunction(service);

        var request = CreateAdminRequest(
            functionContext,
            "POST",
            "https://localhost/api/management/schedules",
            JsonSerializer.Serialize(CreateValidSchedule() with { ServiceOfferId = "srv_missing" }, serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("service_offer_not_found", error.Code);
    }

    [Fact]
    public async Task UpsertSchedule_ThenGetSchedule_ReturnsNormalizedSchedule()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var upsertFunction = new UpsertSlotScheduleFunction(service);
        var getFunction = new GetSlotScheduleFunction(service);

        var upsertRequest = CreateAdminRequest(
            functionContext,
            "POST",
            "https://localhost/api/management/schedules",
            JsonSerializer.Serialize(
                CreateValidSchedule() with { Times = ["09:00", "14:00"] },
                serializerOptions));

        var upsertResponse = await upsertFunction.Run(upsertRequest, CancellationToken.None);
        var savedSchedule = await DeserializeResponseAsync<SlotScheduleDto>(upsertResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);
        Assert.Equal(["09:00", "14:00"], savedSchedule.Times);
        Assert.Equal("srv_consultation", savedSchedule.ServiceOfferId);

        var getRequest = CreateAdminRequest(
            functionContext,
            "GET",
            "https://localhost/api/management/services/srv_consultation/schedule");

        var getResponse = await getFunction.Run(getRequest, "srv_consultation", CancellationToken.None);
        var loadedSchedule = await DeserializeResponseAsync<SlotScheduleDto>(getResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(savedSchedule.ServiceOfferId, loadedSchedule.ServiceOfferId);
        Assert.Equal(savedSchedule.DaysOfWeek, loadedSchedule.DaysOfWeek);
        Assert.Equal(savedSchedule.Times, loadedSchedule.Times);
        Assert.Equal(savedSchedule.SlotDurationMinutes, loadedSchedule.SlotDurationMinutes);
        Assert.Equal(savedSchedule.Capacity, loadedSchedule.Capacity);
        Assert.Equal(savedSchedule.BookingWindowDays, loadedSchedule.BookingWindowDays);
        Assert.Equal(savedSchedule.TimeZoneId, loadedSchedule.TimeZoneId);
    }

    [Fact]
    public async Task GetSchedule_WhenNoneDefined_ReturnsNotFound()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = CreateSerializerOptions();
        var functionContext = new TestFunctionContext(CreateServiceProvider(serializerOptions));
        var getFunction = new GetSlotScheduleFunction(service);

        var newOffer = await service.UpsertServiceOfferAsync(
            new UpsertServiceOfferRequestDto(null, "No Schedule", "Offer without schedule.", 10m, [], true),
            CancellationToken.None);

        var request = CreateAdminRequest(
            functionContext,
            "GET",
            $"https://localhost/api/management/services/{newOffer.Id}/schedule");

        var response = await getFunction.Run(request, newOffer.Id, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("slot_schedule_not_found", error.Code);
    }

    private static SlotScheduleDto CreateValidSchedule()
    {
        return new SlotScheduleDto(
            "srv_consultation",
            [DayOfWeek.Monday, DayOfWeek.Wednesday],
            ["09:00", "14:00"],
            SlotDurationMinutes: 60,
            Capacity: 2,
            BookingWindowDays: 28,
            TimeZoneId: "Europe/Prague",
            Overrides: new Dictionary<string, SlotScheduleOverrideDto>());
    }

    private static TestHttpRequestData CreateAdminRequest(TestFunctionContext functionContext, string method, string url, string? jsonBody = null)
    {
        var request = new TestHttpRequestData(functionContext, method, new Uri(url), jsonBody);
        request.Headers.Add("x-ms-client-principal", CreateClientPrincipalHeaderValue("authenticated", "admin"));
        return request;
    }

    private static JsonSerializerOptions CreateSerializerOptions() => new(JsonSerializerDefaults.Web);

    private static IServiceProvider CreateServiceProvider(JsonSerializerOptions serializerOptions)
    {
        return new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
    }

    private static async Task<T> DeserializeResponseAsync<T>(HttpResponseData response, JsonSerializerOptions serializerOptions)
    {
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(response.Body, serializerOptions);
        return Assert.IsType<T>(result);
    }

    private static string CreateClientPrincipalHeaderValue(params string[] roles)
    {
        var principal = new
        {
            identityProvider = "aad",
            userId = "test-admin-id",
            userDetails = "admin@example.com",
            userRoles = roles
        };

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(principal)));
    }
}
