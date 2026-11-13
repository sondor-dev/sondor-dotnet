using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Sondor.AspNetCore.Benchmarks;

/// <summary>
/// Deciding whether a request wants a problem response (<see cref="ErrorResponseOptions.AppliesTo"/>). It runs for every bare error
/// status, and an unknown URL has no endpoint yet, so a flood of requests for URLs that do not exist reaches it with an
/// <c>Accept</c> header to read: no header at all (a script or a bot), a browser's, and a JSON client's; and with an endpoint, which
/// needs no header. The floor is a delegate that answers without looking.
/// </summary>
[MemoryDiagnoser]
public class ClassifierBenchmarks
{
    private readonly Func<HttpContext, bool> _appliesTo = new ErrorResponseOptions().AppliesTo;
    private readonly Func<HttpContext, bool> _floor = static _ => true;
    private DefaultHttpContext _noHeader = null!;
    private DefaultHttpContext _browser = null!;
    private DefaultHttpContext _json = null!;
    private DefaultHttpContext _minimalApi = null!;
    private DefaultHttpContext _apiController = null!;

    /// <summary>Builds one request of each kind. They are only read, so each is built once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _noHeader = new DefaultHttpContext();

        _browser = new DefaultHttpContext();
        _browser.Request.Headers.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

        _json = new DefaultHttpContext();
        _json.Request.Headers.Accept = "application/json";

        _minimalApi = new DefaultHttpContext();
        _minimalApi.SetEndpoint(new Endpoint(static _ => Task.CompletedTask, EndpointMetadataCollection.Empty, "minimal"));

        _apiController = new DefaultHttpContext();
        _apiController.SetEndpoint(new Endpoint(static _ => Task.CompletedTask, new EndpointMetadataCollection(new ApiMarker()), "api"));
    }

    /// <summary>The floor.</summary>
    [Benchmark(Baseline = true)]
    public bool Floor() => _floor(_noHeader);

    /// <summary>An unknown URL from a client that sends no <c>Accept</c> header.</summary>
    [Benchmark]
    public bool NoEndpointNoHeader() => _appliesTo(_noHeader);

    /// <summary>An unknown URL from a browser.</summary>
    [Benchmark]
    public bool NoEndpointBrowser() => _appliesTo(_browser);

    /// <summary>An unknown URL from a JSON client.</summary>
    [Benchmark]
    public bool NoEndpointJson() => _appliesTo(_json);

    /// <summary>A minimal API endpoint.</summary>
    [Benchmark]
    public bool MinimalApiEndpoint() => _appliesTo(_minimalApi);

    /// <summary>An <c>[ApiController]</c> action.</summary>
    [Benchmark]
    public bool ApiControllerEndpoint() => _appliesTo(_apiController);

    private sealed class ApiMarker : IApiBehaviorMetadata;
}
