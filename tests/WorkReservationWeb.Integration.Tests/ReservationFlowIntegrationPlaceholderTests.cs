﻿using System.Net;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
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

        var getServicesFunction = new GetActiveServiceOffersFunction(service);
        var getSlotsFunction = new GetAvailableSlotsFunction(service);
        var createReservationFunction = new CreateReservationFunction(service);
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

    private static async Task<T> DeserializeResponseAsync<T>(HttpResponseData response, JsonSerializerOptions serializerOptions)
    {
        response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<T>(response.Body, serializerOptions);
        return Assert.IsType<T>(result);
    }
}
