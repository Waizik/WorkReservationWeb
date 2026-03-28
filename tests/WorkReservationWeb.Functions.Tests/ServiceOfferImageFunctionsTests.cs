using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using WorkReservationWeb.Functions.Admin;
using WorkReservationWeb.Functions.Public;
using WorkReservationWeb.Infrastructure.Assets;
using WorkReservationWeb.Shared.Contracts;

namespace WorkReservationWeb.Functions.Tests;

public sealed class ServiceOfferImageFunctionsTests
{
    [Fact]
    public async Task UploadImage_WithoutAdminRole_ReturnsUnauthorized()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storage = new LocalFileServiceOfferImageStorage(tempDirectory.Path);
        var serializerOptions = CreateSerializerOptions();
        var serviceProvider = CreateServiceProvider(serializerOptions);
        var functionContext = new TestFunctionContext(serviceProvider);
        var function = new UploadServiceOfferImageFunction(storage);

        var request = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/service-images"),
            JsonSerializer.Serialize(
                new UploadServiceOfferImageRequestDto("test.png", "image/png", Convert.ToBase64String([1, 2, 3])),
                serializerOptions));

        var response = await function.Run(request, CancellationToken.None);
        var error = await DeserializeResponseAsync<ApiErrorDto>(response, serializerOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task UploadImage_ThenGetImage_ReturnsStoredContent()
    {
        using var tempDirectory = new TemporaryDirectory();
        var storage = new LocalFileServiceOfferImageStorage(tempDirectory.Path);
        var serializerOptions = CreateSerializerOptions();
        var serviceProvider = CreateServiceProvider(serializerOptions);
        var functionContext = new TestFunctionContext(serviceProvider);
        var uploadFunction = new UploadServiceOfferImageFunction(storage);
        var getFunction = new GetServiceOfferImageFunction(storage);
        var imageBytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };

        var uploadRequest = new TestHttpRequestData(
            functionContext,
            "POST",
            new Uri("https://localhost/api/management/service-images"),
            JsonSerializer.Serialize(
                new UploadServiceOfferImageRequestDto("offer.png", "image/png", Convert.ToBase64String(imageBytes)),
                serializerOptions));
        uploadRequest.Headers.Add("x-ms-client-principal", CreateClientPrincipalHeaderValue("authenticated", "admin"));

        var uploadResponse = await uploadFunction.Run(uploadRequest, CancellationToken.None);
        var uploadResult = await DeserializeResponseAsync<ServiceOfferImageUploadResultDto>(uploadResponse, serializerOptions);

        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        Assert.Equal("offer.png", uploadResult.FileName);
        Assert.Equal("image/png", uploadResult.ContentType);
        Assert.Equal(imageBytes.LongLength, uploadResult.ContentLength);
        Assert.Equal($"https://localhost/api/public/assets/{uploadResult.AssetId}", uploadResult.Url);

        var getRequest = new TestHttpRequestData(
            functionContext,
            "GET",
            new Uri(uploadResult.Url));

        var getResponse = await getFunction.Run(getRequest, uploadResult.AssetId, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Contains(getResponse.Headers, header => string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) && header.Value.Contains("image/png"));

        getResponse.Body.Position = 0;
        using var memoryStream = new MemoryStream();
        await getResponse.Body.CopyToAsync(memoryStream, CancellationToken.None);
        Assert.Equal(imageBytes, memoryStream.ToArray());
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wrw-img-{Guid.NewGuid():N}");
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