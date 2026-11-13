using System.Net;
using Microsoft.Extensions.Logging;

namespace Sondor.Http.Tests;

internal sealed class CollectingProvider(List<LogEntry> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Collector(entries);

    public void Dispose()
    {
    }

    private sealed class Collector(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add(new LogEntry(logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
    }
}

/// <summary>What a call logs, and what it never does.</summary>
[TestFixture]
public sealed class LoggingTests
{
    private const string SecretUrl = "https://orders.example.com/customers/jane@example.com/cards?token=SECRET-QUERY&key=SECRET-KEY";

    private static async Task<List<LogEntry>> CallAsync(Func<HttpResponseMessage> respond, Action<HttpResultOptions>? configure = null, bool secretUrl = true)
    {
        var logger = new ListLogger();
        var handler = new StubHandler((request, ct) =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer SECRET-HEADER");

            return Task.FromResult(respond());
        });
        var client = Clients.Create(handler, configure, logger);

        using var request = new HttpRequestMessage(HttpMethod.Get, secretUrl ? SecretUrl : "orders");
        request.Content = new StringContent("SECRET-REQUEST-BODY");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "SECRET-API-KEY");

        await client.SendAsync(request);

        return logger.Entries;
    }

    private static void AssertNoSecrets(List<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            Assert.That(entry.Message, Does.Not.Contain("SECRET"));
            Assert.That(entry.Message, Does.Not.Contain("token="));
            Assert.That(entry.Message, Does.Not.Contain("jane@example.com"));
            Assert.That(entry.Message, Does.Not.Contain("/customers"));
        }
    }

    [Test]
    public async Task Call_Should_LogASuccessAtDebug_WithTheHostAndTheStatusOnly()
    {
        var entries = await CallAsync(() => Responses.Empty(HttpStatusCode.OK));

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(entries[0].Message, Does.StartWith("GET orders.example.com answered 200 in "));
        AssertNoSecrets(entries);
    }

    [Test]
    public async Task Call_Should_LogARefusalAtInformation_WithTheCode()
    {
        var entries = await CallAsync(() => Responses.Problem(HttpStatusCode.NotFound, """{"errorCode":"Order.NotFound","detail":"SECRET-DETAIL for jane@example.com","traceId":"SECRET-TRACE"}"""));

        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(entries[0].Message, Does.Contain("404").And.Contain("Order.NotFound"));
        AssertNoSecrets(entries);
    }

    [Test]
    public async Task Call_Should_LogAServerErrorAtWarning()
    {
        var entries = await CallAsync(() => Responses.Json(HttpStatusCode.InternalServerError, "SECRET-BODY at /customers", "text/plain"));

        Assert.That(entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entries[0].Message, Does.Contain("500").And.Contain("Error.Internal"));
        AssertNoSecrets(entries);
    }

    [Test]
    public async Task Call_Should_LogASuccessThatCannotBeUsedAtWarning()
    {
        var logger = new ListLogger();
        var client = Clients.Create(new StubHandler(() => Responses.Json(HttpStatusCode.OK, "SECRET", "text/html")), logger: logger);

        await client.GetAsync<Order>($"customers?token=SECRET-QUERY");

        Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logger.Entries[0].Message, Does.Contain("200").And.Contain("Http.InvalidResponse"));
        AssertNoSecrets(logger.Entries);
    }

    [Test]
    public async Task Call_Should_LogAFailureWithoutAResponseAtWarning_WithoutTheExceptionMessage()
    {
        var logger = new ListLogger();
        var client = Clients.Create(new StubHandler((_, _) => throw new HttpRequestException("connect to jane@example.com failed: SECRET")), logger: logger);

        await client.GetAsync<Order>("customers?token=SECRET-QUERY");

        Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(logger.Entries[0].Message, Does.Contain("Http.Unavailable").And.Contain("without a usable response"));
        AssertNoSecrets(logger.Entries);
    }

    [Test]
    public async Task Call_Should_LogNothing_When_LoggingIsSwitchedOff()
    {
        var entries = await CallAsync(() => Responses.Empty(HttpStatusCode.OK), options => options.LogCalls = false);

        Assert.That(entries, Is.Empty);
    }

    [Test]
    public async Task Client_Should_LogNothing_When_ItHasNoLogger()
    {
        var client = new HttpResultClient(new HttpClient(new StubHandler(() => Responses.Empty(HttpStatusCode.OK))) { BaseAddress = Clients.OrdersAddress });

        Assert.That((await client.DeleteAsync("a")).IsSuccess, Is.True);
    }
}
