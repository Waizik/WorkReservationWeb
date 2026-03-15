using System.Collections;
using System.Security.Claims;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;

namespace WorkReservationWeb.Integration.Tests;

internal sealed class TestInvocationFeatures : IInvocationFeatures
{
    private readonly Dictionary<Type, object> features = [];

    public T? Get<T>()
    {
        return features.TryGetValue(typeof(T), out var feature)
            ? (T)feature
            : default;
    }

    public void Set<T>(T instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        features[typeof(T)] = instance!;
    }

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        return features.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

internal sealed class TestFunctionContext : FunctionContext
{
    private readonly IServiceProvider serviceProvider;
    private readonly Dictionary<object, object> items = [];

    public TestFunctionContext(IServiceProvider? serviceProvider = null)
    {
        this.serviceProvider = serviceProvider ?? new ServiceCollection().BuildServiceProvider();
    }

    public override string InvocationId => Guid.NewGuid().ToString("N");

    public override string FunctionId => "test-function";

    public override TraceContext TraceContext => throw new NotSupportedException();

    public override BindingContext BindingContext => throw new NotSupportedException();

    public override RetryContext RetryContext => null!;

    public override IServiceProvider InstanceServices
    {
        get => serviceProvider;
        set => throw new NotSupportedException();
    }

    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();

    public override IDictionary<object, object> Items
    {
        get => items;
        set => throw new NotSupportedException();
    }

    public override IInvocationFeatures Features { get; } = new TestInvocationFeatures();
}

internal sealed class TestHttpRequestData : HttpRequestData
{
    private readonly MemoryStream bodyStream;
    private readonly Uri url;
    private readonly HttpHeadersCollection headers;

    public TestHttpRequestData(FunctionContext functionContext, string method, Uri url, string? jsonBody = null)
        : base(functionContext)
    {
        this.url = url;
        headers = new HttpHeadersCollection();
        bodyStream = new MemoryStream();

        if (!string.IsNullOrWhiteSpace(jsonBody))
        {
            using var writer = new StreamWriter(bodyStream, leaveOpen: true);
            writer.Write(jsonBody);
            writer.Flush();
            bodyStream.Position = 0;
            headers.Add("Content-Type", "application/json");
        }

        Method = method;
    }

    public override Stream Body => bodyStream;

    public override HttpHeadersCollection Headers => headers;

    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();

    public override Uri Url => url;

    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();

    public override string Method { get; }

    public override HttpResponseData CreateResponse()
    {
        return new TestHttpResponseData(FunctionContext);
    }
}

internal sealed class TestHttpResponseData : HttpResponseData
{
    public TestHttpResponseData(FunctionContext functionContext)
        : base(functionContext)
    {
        Headers = new HttpHeadersCollection();
        Body = new MemoryStream();
        Cookies = new TestHttpCookies();
    }

    public override HttpStatusCode StatusCode { get; set; }

    public override HttpHeadersCollection Headers { get; set; }

    public override Stream Body { get; set; }

    public override HttpCookies Cookies { get; }
}

internal sealed class TestHttpCookies : HttpCookies
{
    private readonly List<IHttpCookie> cookies = [];

    public override void Append(string name, string value)
    {
        cookies.Add(new HttpCookie(name, value));
    }

    public override void Append(IHttpCookie cookie)
    {
        cookies.Add(cookie);
    }

    public override IHttpCookie CreateNew()
    {
        return new HttpCookie(string.Empty, string.Empty);
    }
}