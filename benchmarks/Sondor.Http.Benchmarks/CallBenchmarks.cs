using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Sondor.Http.Benchmarks;

/// <summary>An order, as the other side sends it.</summary>
public sealed record Order(Guid Id, string Name, decimal Total);

/// <summary>Answers every request with the same bytes, so what is measured is the client and not a socket.</summary>
internal sealed class CannedHandler(HttpStatusCode status, byte[] body, string mediaType) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);

        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);

        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    }
}

/// <summary>
/// One call, each against the same thing written by hand with <c>HttpClient</c> and <c>System.Net.Http.Json</c>: a small success, a
/// large success, a not found with a problem document (the hand-written version also reads the error code, which is the work this
/// package does) and a server error with an HTML page (neither reads it). The handler answers from memory. Every case runs after 0 and
/// after 10,000 earlier calls on the same client: a cost that grows with the calls already made would show as a difference between
/// the two.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CallBenchmarks
{
    private static readonly Uri Base = new("https://orders.example.com/");

    private HttpClient _smallPlain = null!;
    private HttpClient _largePlain = null!;
    private HttpClient _missingPlain = null!;
    private HttpClient _brokenPlain = null!;
    private HttpResultClient _small = null!;
    private HttpResultClient _large = null!;
    private HttpResultClient _missing = null!;
    private HttpResultClient _broken = null!;

    /// <summary>How many calls of each kind are made before measuring.</summary>
    [Params(0, 10_000)]
    public int Earlier { get; set; }

    /// <summary>Builds the clients and makes the earlier calls.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var order = JsonSerializer.SerializeToUtf8Bytes(new Order(Guid.NewGuid(), "Widget", 12.5m), JsonSerializerOptions.Web);
        var orders = JsonSerializer.SerializeToUtf8Bytes(
            Enumerable.Range(0, 500).Select(i => new Order(Guid.NewGuid(), "Widget " + i, i)).ToList(),
            JsonSerializerOptions.Web);
        var problem = Encoding.UTF8.GetBytes(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.5\",\"title\":\"Not Found\",\"status\":404," +
            "\"detail\":\"No Order was found for the given Id.\",\"instance\":\"/orders/1\",\"errorCode\":\"Order.NotFound\"," +
            "\"traceId\":\"00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01\"}");
        var html = Encoding.UTF8.GetBytes("<html><body>Bad gateway</body></html>");

        _smallPlain = Plain(HttpStatusCode.OK, order, "application/json");
        _largePlain = Plain(HttpStatusCode.OK, orders, "application/json");
        _missingPlain = Plain(HttpStatusCode.NotFound, problem, "application/problem+json");
        _brokenPlain = Plain(HttpStatusCode.BadGateway, html, "text/html");
        _small = Sondor(HttpStatusCode.OK, order, "application/json");
        _large = Sondor(HttpStatusCode.OK, orders, "application/json");
        _missing = Sondor(HttpStatusCode.NotFound, problem, "application/problem+json");
        _broken = Sondor(HttpStatusCode.BadGateway, html, "text/html");

        for (var call = 0; call < Earlier; call++)
        {
            await SmallPlain();
            await LargePlain();
            await MissingPlain();
            await BrokenPlain();
            await SmallSondor();
            await LargeSondor();
            await MissingSondor();
            await BrokenSondor();
        }
    }

    /// <summary>A small success, by hand.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Small success")]
    public async Task<Order?> SmallPlain() => await GetByHand<Order>(_smallPlain);

    /// <summary>A small success, with this package.</summary>
    [Benchmark, BenchmarkCategory("Small success")]
    public async Task<Result<Order>> SmallSondor() => await _small.GetAsync<Order>("orders/1");

    /// <summary>A large success (500 orders, about 55 KB), by hand.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Large success")]
    public async Task<List<Order>?> LargePlain() => await GetByHand<List<Order>>(_largePlain);

    /// <summary>A large success, with this package: the same read, bounded.</summary>
    [Benchmark, BenchmarkCategory("Large success")]
    public async Task<Result<List<Order>>> LargeSondor() => await _large.GetAsync<List<Order>>("orders");

    /// <summary>A not found with a problem document, by hand: the status, and the error code read from the body.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Not found")]
    public async Task<string?> MissingPlain()
    {
        using var response = await _missingPlain.GetAsync("orders/1", HttpCompletionOption.ResponseHeadersRead);

        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var body = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(body);

        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : "Request.NotFound";
    }

    /// <summary>A not found with a problem document, with this package: a typed error with its context.</summary>
    [Benchmark, BenchmarkCategory("Not found")]
    public async Task<Result<Order>> MissingSondor() => await _missing.GetAsync<Order>("orders/1");

    /// <summary>A bad gateway with an HTML page, by hand: the status only.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Bad gateway")]
    public async Task<int> BrokenPlain()
    {
        using var response = await _brokenPlain.GetAsync("orders/1", HttpCompletionOption.ResponseHeadersRead);

        return (int)response.StatusCode;
    }

    /// <summary>A bad gateway with an HTML page, with this package: an error from the status; the page is not read.</summary>
    [Benchmark, BenchmarkCategory("Bad gateway")]
    public async Task<Result<Order>> BrokenSondor() => await _broken.GetAsync<Order>("orders/1");

    private static async Task<T?> GetByHand<T>(HttpClient http)
    {
        using var response = await http.GetAsync("orders", HttpCompletionOption.ResponseHeadersRead);

        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }

    private static HttpClient Plain(HttpStatusCode status, byte[] body, string mediaType) =>
        new(new CannedHandler(status, body, mediaType)) { BaseAddress = Base };

    private static HttpResultClient Sondor(HttpStatusCode status, byte[] body, string mediaType) =>
        new(Plain(status, body, mediaType), new HttpResultOptions { LogCalls = false });
}
