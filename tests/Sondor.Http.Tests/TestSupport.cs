using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sondor.Http.Tests;

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a delegate and remembers what it was asked.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public StubHandler(Func<HttpResponseMessage> respond)
        : this((_, _) => Task.FromResult(respond()))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return respond(request, cancellationToken);
    }
}

internal static class Responses
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string json) => Json(status, json, "application/problem+json");

    public static HttpResponseMessage Empty(HttpStatusCode status) => new(status);
}

/// <summary>A body that has no length (as a chunked one has) and produces as many bytes as it is told to.</summary>
internal sealed class EndlessContent(long length) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var chunk = Encoding.UTF8.GetBytes(new string('x', 1024));
        var written = 0L;

        while (written < length)
        {
            await stream.WriteAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, length - written)));
            written += chunk.Length;
        }
    }

    protected override bool TryComputeLength(out long computed)
    {
        computed = 0;

        return false;
    }

    public static EndlessContent Of(long length, string mediaType = "application/json")
    {
        var content = new EndlessContent(length);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);

        return content;
    }
}

/// <summary>A body whose connection drops while it is read.</summary>
internal sealed class BrokenContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new IOException("The connection was closed while the body was read.");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}

internal sealed record LogEntry(LogLevel Level, string Message);

internal sealed class ListLogger : ILogger<HttpResultClient>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add(new LogEntry(logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
}

internal static class Clients
{
    public static readonly Uri OrdersAddress = new("https://orders.example.com/");

    public static HttpResultClient Create(
        StubHandler handler,
        Action<HttpResultOptions>? configure = null,
        ILogger<HttpResultClient>? logger = null,
        TimeProvider? time = null)
    {
        var options = new HttpResultOptions();

        configure?.Invoke(options);

        return new HttpResultClient(new HttpClient(handler) { BaseAddress = OrdersAddress }, options, logger, time);
    }

    public static HttpResultClient Create(Func<HttpResponseMessage> respond, Action<HttpResultOptions>? configure = null) =>
        Create(new StubHandler(respond), configure);
}

internal sealed record Order(Guid Id, string Name);
