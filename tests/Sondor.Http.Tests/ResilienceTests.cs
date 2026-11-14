using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Sondor.Http.Tests;

/// <summary>
/// The recipe of the README, against the framework's real resilience handlers: this package does not depend on them, and its
/// settings and exception mappings work with them.
/// </summary>
[TestFixture]
public sealed class ResilienceTests
{
    private static ServiceProvider Build(StubHandler handler, Action<HttpResultOptions>? configure = null)
    {
        var services = new ServiceCollection().AddLogging();

        services.AddSondorHttpClient("orders", options =>
            {
                options.BaseAddress = Clients.OrdersAddress;
                configure?.Invoke(options);
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                options.Retry.DisableForUnsafeHttpMethods();
                options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(500);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMilliseconds(500);
            });

        return services.BuildServiceProvider();
    }

    private static HttpResultClient ClientOf(ServiceProvider provider) =>
        provider.GetRequiredService<IHttpResultClientFactory>().CreateClient("orders");

    [Test]
    public async Task Exceptions_Should_ReportATimeoutOfTheResilienceHandler_AsAValue_WhenItIsMapped()
    {
        var handler = new StubHandler((_, ct) => Task.Delay(Timeout.Infinite, ct).ContinueWith<HttpResponseMessage>(_ => throw new InvalidOperationException(), ct));
        await using var mapped = Build(handler, options => options.Exceptions
            .Map<TimeoutRejectedException>(_ => new Error("Http.Timeout", "The call took longer than allowed.", ErrorType.Failure)));
        await using var unmapped = Build(handler);

        var result = await ClientOf(mapped).GetAsync<Order>("orders/1");

        Assert.That(result.Error.Code, Is.EqualTo("Http.Timeout"));
        await Assert.ThrowsAsync<TimeoutRejectedException>(() => ClientOf(unmapped).GetAsync<Order>("orders/1"));
    }

    [Test]
    public async Task Exceptions_Should_ReportAnOpenCircuit_AsAValue_WhenItIsMapped()
    {
        await using var provider = Build(
            new StubHandler((_, _) => throw new BrokenCircuitException()),
            options => options.Exceptions.Map<BrokenCircuitException>(_ => new Error("Http.Unavailable", "The service is not available.", ErrorType.Failure)));

        var result = await ClientOf(provider).GetAsync<Order>("orders/1");

        Assert.That(result.Error.Code, Is.EqualTo("Http.Unavailable"));
    }

    [Test]
    public async Task DisableForUnsafeHttpMethods_Should_RetryAGet_ButNotAPost()
    {
        var gets = 0;
        var posts = 0;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                gets++;
            }
            else
            {
                posts++;
            }

            return Task.FromResult(Responses.Problem(HttpStatusCode.ServiceUnavailable, """{"errorCode":"Orders.Busy"}"""));
        });
        await using var provider = Build(handler);
        var client = ClientOf(provider);

        var get = await client.GetAsync<Order>("orders/1");
        var post = await client.PostAsync("orders", new Order(Guid.Empty, "Widget"));

        Assert.That(get.Error.Code, Is.EqualTo("Orders.Busy"));
        Assert.That(post.Error.Code, Is.EqualTo("Orders.Busy"));
        Assert.That(gets, Is.GreaterThan(1), "a safe method is retried");
        Assert.That(posts, Is.EqualTo(1), "an unsafe one is not");
    }
}
