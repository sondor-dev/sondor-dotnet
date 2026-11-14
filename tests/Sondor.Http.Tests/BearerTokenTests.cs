using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Http.Tests;

/// <summary>A token goes where it is allowed to go, and nowhere else.</summary>
[TestFixture]
public sealed class BearerTokenTests
{
    private static ServiceProvider Build(
        StubHandler handler,
        Func<HttpRequestMessage, CancellationToken, ValueTask<string?>>? getToken = null,
        Action<BearerTokenOptions>? configure = null,
        Action<HttpResultOptions>? client = null)
    {
        var services = new ServiceCollection().AddLogging();

        services.AddSondorHttpClient("orders", options =>
            {
                options.BaseAddress = Clients.OrdersAddress;
                client?.Invoke(options);
            })
            .AddSondorBearerToken(getToken ?? ((_, _) => ValueTask.FromResult<string?>("token-1")), configure)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private static HttpResultClient ClientOf(ServiceProvider provider) =>
        provider.GetRequiredService<IHttpResultClientFactory>().CreateClient("orders");

    [Test]
    public async Task AddSondorBearerToken_Should_SetTheHeaderOnTheRequest_NotOnTheClientsDefaults()
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler);

        await ClientOf(provider).DeleteAsync("orders/1");
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("orders");

        Assert.That(handler.Requests[0].Headers.Authorization?.ToString(), Is.EqualTo("Bearer token-1"));
        Assert.That(http.DefaultRequestHeaders.Authorization, Is.Null);
    }

    [Test]
    public async Task AddSondorBearerToken_Should_AskTheProviderForEveryRequest()
    {
        var calls = 0;
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler, (_, _) => ValueTask.FromResult<string?>($"token-{++calls}"));
        var client = ClientOf(provider);

        await client.DeleteAsync("a");
        await client.DeleteAsync("b");

        Assert.That(string.Join(",", handler.Requests.Select(r => r.Headers.Authorization?.Parameter)), Is.EqualTo("token-1,token-2"));
    }

    [Test]
    public async Task AddSondorBearerToken_Should_NotSendTheToken_ToAHostThatIsNotAllowed()
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler);
        var client = ClientOf(provider);

        using var other = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.net/next?page=2");
        using var same = new HttpRequestMessage(HttpMethod.Get, "https://ORDERS.example.com/next");

        await client.SendAsync(other);
        await client.SendAsync(same);

        Assert.That(handler.Requests[0].Headers.Authorization, Is.Null);
        Assert.That(handler.Requests[1].Headers.Authorization, Is.Not.Null);
    }

    [Test]
    public async Task AddSondorBearerToken_Should_UseTheHostsThatAreListed_InsteadOfTheBaseAddress()
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler, configure: options => options.AllowedHosts.Add("Billing.Example.com"));
        var client = ClientOf(provider);

        using var billing = new HttpRequestMessage(HttpMethod.Get, "https://billing.example.com/invoices");
        using var orders = new HttpRequestMessage(HttpMethod.Get, "https://orders.example.com/orders");

        await client.SendAsync(billing);
        await client.SendAsync(orders);

        Assert.That(handler.Requests[0].Headers.Authorization, Is.Not.Null);
        Assert.That(handler.Requests[1].Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task AddSondorBearerToken_Should_NotSendTheToken_OverPlainHttp_UnlessAllowed()
    {
        var plain = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        var allowed = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var strict = Build(plain, client: options => options.BaseAddress = new Uri("http://orders.example.com/"));
        await using var local = Build(allowed, configure: options => options.AllowInsecureHttp = true, client: options => options.BaseAddress = new Uri("http://orders.example.com/"));

        await ClientOf(strict).DeleteAsync("a");
        await ClientOf(local).DeleteAsync("a");

        Assert.That(plain.Requests[0].Headers.Authorization, Is.Null);
        Assert.That(allowed.Requests[0].Headers.Authorization, Is.Not.Null);
    }

    [Test]
    public async Task AddSondorBearerToken_Should_KeepTheAuthorizationHeaderTheRequestHas()
    {
        var calls = 0;
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler, (_, _) =>
        {
            calls++;

            return ValueTask.FromResult<string?>("token-1");
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "a");
        request.Headers.Authorization = new("Basic", "abc");

        await ClientOf(provider).SendAsync(request);

        Assert.That(handler.Requests[0].Headers.Authorization?.Scheme, Is.EqualTo("Basic"));
        Assert.That(calls, Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task AddSondorBearerToken_Should_SendNoHeader_When_TheProviderHasNoToken(string? token)
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        await using var provider = Build(handler, (_, _) => ValueTask.FromResult(token));

        await ClientOf(provider).DeleteAsync("a");

        Assert.That(handler.Requests[0].Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task AddSondorBearerToken_Should_UseAProviderType()
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(new TokenSource("from-a-type"));
        services.AddSondorHttpClient("orders", options => options.BaseAddress = Clients.OrdersAddress)
            .AddSondorBearerToken<TypedProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        await ClientOf(provider).DeleteAsync("a");

        Assert.That(handler.Requests[0].Headers.Authorization?.Parameter, Is.EqualTo("from-a-type"));
    }

    [Test]
    public async Task AddSondorBearerToken_Should_LogWhyNoTokenWasSent_WithoutTheTokenOrThePath()
    {
        var handler = new StubHandler(() => Responses.Empty(HttpStatusCode.OK));
        var logs = new List<LogEntry>();
        var services = new ServiceCollection().AddLogging(logging => logging.AddProvider(new CollectingProvider(logs)));
        services.AddSondorHttpClient("orders", options => options.BaseAddress = Clients.OrdersAddress)
            .AddSondorBearerToken((_, _) => ValueTask.FromResult<string?>("SECRET-TOKEN"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.net/private/path?key=SECRET-QUERY");

        await ClientOf(provider).SendAsync(request);

        var line = logs.Single(entry => entry.Message.Contains("No token", StringComparison.Ordinal));

        Assert.That(line.Level, Is.EqualTo(Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.That(line.Message, Does.Contain("evil.example.net").And.Contain("the host is not allowed"));
        Assert.That(line.Message, Does.Not.Contain("/private"));
        Assert.That(logs.Select(entry => entry.Message), Has.None.Contain("SECRET"));
    }

    [Test]
    public async Task AddSondorHttpClient_Should_NotStartTheHost_When_ATokenHasNowhereToGo()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSondorHttpClient("orders").AddSondorBearerToken((_, _) => ValueTask.FromResult<string?>("t"));

        using var host = builder.Build();
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(failure!.Message, Does.Contain("orders").And.Contain("AllowedHosts"));
    }

    [Test]
    public void AddSondorBearerToken_Should_RejectBadArguments()
    {
        var builder = new ServiceCollection().AddSondorHttpClient("orders");

        Assert.Throws<ArgumentNullException>(() => BearerTokenExtensions.AddSondorBearerToken<TypedProvider>(null!));
        Assert.Throws<ArgumentNullException>(() => BearerTokenExtensions.AddSondorBearerToken(null!, (_, _) => ValueTask.FromResult<string?>("t")));
        Assert.Throws<ArgumentNullException>(() => builder.AddSondorBearerToken(null!));
    }

    private sealed record TokenSource(string Token);

    private sealed class TypedProvider(TokenSource source) : IAccessTokenProvider
    {
        public ValueTask<string?> GetTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(source.Token);
    }
}
