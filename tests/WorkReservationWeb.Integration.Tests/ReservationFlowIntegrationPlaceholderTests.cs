﻿using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using WorkReservationWeb.Infrastructure.Notifications;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Functions.Public;
using WorkReservationWeb.Infrastructure.Services;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Integration.Tests;

public class ReservationFlowIntegrationTests
{
    [Fact]
    public async Task AdminReservations_WithoutAdminRole_ReturnsUnauthorized()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        var getReservationsFunction = new GetReservationsFunction(service);

        var request = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/management/reservations"));
        request.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated"));

        var response = await getReservationsFunction.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task ReservationFlow_EndToEnd_CreatesReservationAndShowsItInAdminList()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        using var sentEmailDirectory = new TemporaryDirectory();

        var getServicesFunction = new GetActiveServiceOffersFunction(service);
        var getSlotsFunction = new GetAvailableSlotsFunction(service);
        var createReservationFunction = new CreateReservationFunction(service, new LocalDevelopmentReservationNotificationService(sentEmailDirectory.Path));
        var getReservationsFunction = new GetReservationsFunction(service);

        var getServicesRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/public/services"));

        var servicesResponse = await getServicesFunction.Run(getServicesRequest, CancellationToken.None);
        var serviceOffers = await DeserializeResponseAsync<List<ServiceOfferDto>>(servicesResponse, serializerOptions);

        var selectedService = Assert.Single(serviceOffers);

        var getSlotsRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri($"https://localhost/api/public/services/{selectedService.Id}/slots"));

        var slotsResponse = await getSlotsFunction.Run(getSlotsRequest, selectedService.Id, CancellationToken.None);
        var availableSlots = await DeserializeResponseAsync<List<ReservationSlotDto>>(slotsResponse, serializerOptions);

        var selectedSlot = Assert.Single(availableSlots.Where(slot => slot.ServiceOfferId == selectedService.Id).Take(1));

        var createRequestPayload = new CreateReservationRequestDto(
            selectedService.Id,
            selectedSlot.Id,
            selectedSlot.Etag,
            "Integration User",
            "integration@example.com",
            "Created by integration test");

        var createReservationRequest = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(createRequestPayload, serializerOptions));

        var createReservationResponse = await createReservationFunction.Run(createReservationRequest, CancellationToken.None);
        var createReservationResult = await DeserializeResponseAsync<CreateReservationResultDto>(createReservationResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.Created, createReservationResponse.StatusCode);
        Assert.True(createReservationResult.Success);
        Assert.Equal(ReservationCreateOutcome.Created, createReservationResult.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(createReservationResult.ReservationId));
        Assert.False(string.IsNullOrWhiteSpace(createReservationResult.UpdatedSlotEtag));

        var getReservationsRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/management/reservations"));
        getReservationsRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var reservationsResponse = await getReservationsFunction.Run(getReservationsRequest, CancellationToken.None);
        var reservations = await DeserializeResponseAsync<List<ReservationSummaryDto>>(reservationsResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, reservationsResponse.StatusCode);
        var reservation = Assert.Single(reservations);
        Assert.Equal(createReservationResult.ReservationId, reservation.Id);
        Assert.Equal(selectedService.Id, reservation.ServiceOfferId);
        Assert.Equal(selectedSlot.Id, reservation.SlotId);
        Assert.Equal("Integration User", reservation.CustomerName);
        Assert.Equal("integration@example.com", reservation.CustomerEmail);
        Assert.Equal("Confirmed", reservation.Status);
    }

    [Fact]
    public async Task InactiveServiceOffer_IsVisibleToAdminButHiddenFromPublicBooking()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        using var sentEmailDirectory = new TemporaryDirectory();

        var getServicesFunction = new GetActiveServiceOffersFunction(service);
        var getManagementServicesFunction = new GetServiceOffersFunction(service);
        var getSlotsFunction = new GetAvailableSlotsFunction(service);
        var createReservationFunction = new CreateReservationFunction(service, new LocalDevelopmentReservationNotificationService(sentEmailDirectory.Path));
        var upsertServiceOfferFunction = new UpsertServiceOfferFunction(service);

        var deactivatePayload = new UpsertServiceOfferRequestDto(
            "srv_consultation",
            "Consultation",
            "Initial consultation meeting.",
            49m,
            ["https://example.invalid/images/consultation.jpg"],
            false);

        var upsertRequest = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/services"),
            JsonSerializer.Serialize(deactivatePayload, serializerOptions));
        upsertRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var upsertResponse = await upsertServiceOfferFunction.Run(upsertRequest, CancellationToken.None);
        var updatedServiceOffer = await DeserializeResponseAsync<ServiceOfferDto>(upsertResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, upsertResponse.StatusCode);
        Assert.False(updatedServiceOffer.Active);

        var publicServicesRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/public/services"));

        var publicServicesResponse = await getServicesFunction.Run(publicServicesRequest, CancellationToken.None);
        var publicServiceOffers = await DeserializeResponseAsync<List<ServiceOfferDto>>(publicServicesResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, publicServicesResponse.StatusCode);
        Assert.Empty(publicServiceOffers);

        var managementServicesRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/management/services"));
        managementServicesRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var managementServicesResponse = await getManagementServicesFunction.Run(managementServicesRequest, CancellationToken.None);
        var managementServiceOffers = await DeserializeResponseAsync<List<ServiceOfferDto>>(managementServicesResponse, serializerOptions);

        var managementOffer = Assert.Single(managementServiceOffers);
        Assert.Equal(HttpStatusCode.OK, managementServicesResponse.StatusCode);
        Assert.Equal("srv_consultation", managementOffer.Id);
        Assert.False(managementOffer.Active);

        var getSlotsRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/public/services/srv_consultation/slots"));

        var slotsResponse = await getSlotsFunction.Run(getSlotsRequest, "srv_consultation", CancellationToken.None);
        var slots = await DeserializeResponseAsync<List<ReservationSlotDto>>(slotsResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.OK, slotsResponse.StatusCode);
        Assert.Empty(slots);

        var createRequestPayload = new CreateReservationRequestDto(
            "srv_consultation",
            "slot_202603160800",
            "ignored-etag",
            "Inactive User",
            "inactive@example.com",
            null);

        var createReservationRequest = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/public/reservations"),
            JsonSerializer.Serialize(createRequestPayload, serializerOptions));

        var createReservationResponse = await createReservationFunction.Run(createReservationRequest, CancellationToken.None);
        var createReservationResult = await DeserializeResponseAsync<CreateReservationResultDto>(createReservationResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.BadRequest, createReservationResponse.StatusCode);
        Assert.False(createReservationResult.Success);
        Assert.Equal(ReservationCreateOutcome.ValidationFailed, createReservationResult.Outcome);
        Assert.Equal("Selected service is not available.", createReservationResult.Message);
    }

    [Fact]
    public async Task DeleteServiceOffer_WithNoLinkedSlotsOrReservations_RemovesItFromManagementList()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);

        var upsertServiceOfferFunction = new UpsertServiceOfferFunction(service);
        var deleteServiceOfferFunction = new DeleteServiceOfferFunction(service);
        var getServiceOffersFunction = new GetServiceOffersFunction(service);

        var createPayload = new UpsertServiceOfferRequestDto(
            null,
            "Deletable Offer",
            "Offer without linked slots.",
            10m,
            [],
            true);

        var createRequest = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/services"),
            JsonSerializer.Serialize(createPayload, serializerOptions));
        createRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var createResponse = await upsertServiceOfferFunction.Run(createRequest, CancellationToken.None);
        var createdOffer = await DeserializeResponseAsync<ServiceOfferDto>(createResponse, serializerOptions);

        var deleteRequest = new TestHttpRequestData(
            functionContext,
            "DELETE",
            new Uri($"https://localhost/api/management/services/{createdOffer.Id}"));
        deleteRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var deleteResponse = await deleteServiceOfferFunction.Run(deleteRequest, createdOffer.Id, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getServiceOffersRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri("https://localhost/api/management/services"));
        getServiceOffersRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var getServiceOffersResponse = await getServiceOffersFunction.Run(getServiceOffersRequest, CancellationToken.None);
        var serviceOffers = await DeserializeResponseAsync<List<ServiceOfferDto>>(getServiceOffersResponse, serializerOptions);

        Assert.DoesNotContain(serviceOffers, serviceOffer => serviceOffer.Id == createdOffer.Id);
    }

    [Fact]
    public async Task DeleteServiceOffer_WithLinkedSlots_ReturnsConflict()
    {
        var service = new InMemoryReservationPlatformService();
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serviceProvider = new ServiceCollection()
            .AddOptions()
            .AddSingleton(serializerOptions)
            .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer(serializerOptions))
            .BuildServiceProvider();
        var functionContext = new TestFunctionContext(serviceProvider);
        var deleteServiceOfferFunction = new DeleteServiceOfferFunction(service);

        var deleteRequest = new TestHttpRequestData(
            functionContext,
            "DELETE",
            new Uri("https://localhost/api/management/services/srv_consultation"));
        deleteRequest.Headers.Add("x-ms-client-principal", TestStaticWebAppsPrincipalFactory.CreateHeaderValue("authenticated", "admin"));

        var deleteResponse = await deleteServiceOfferFunction.Run(deleteRequest, "srv_consultation", CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(deleteResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        Assert.Equal("service_offer_in_use", error.Code);
    }

    private static async Task<T> DeserializeResponseAsync<T>(HttpResponseData response, JsonSerializerOptions serializerOptions)
    {
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(response.Body, serializerOptions);
        return Assert.IsType<T>(result);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wrw-int-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
