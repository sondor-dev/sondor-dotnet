using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sondor.AspNetCore.Tests;

/// <summary>A real pipeline on an in-memory server, so each test exercises the same path an application would.</summary>
internal sealed class TestApp : IAsyncDisposable
{
    private TestApp(WebApplication app, HttpClient client, List<LogEntry> logs)
    {
        App = app;
        Client = client;
        Logs = logs;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public List<LogEntry> Logs { get; }

    /// <summary>Starts an application. <paramref name="register"/> adds services, <paramref name="map"/> adds endpoints.</summary>
    public static async Task<TestApp> StartAsync(
        Action<ErrorResponseOptions>? configure = null,
        Action<IServiceCollection>? register = null,
        Action<WebApplication>? map = null,
        bool mvc = false,
        bool usePackage = true,
        bool addPackage = true,
        Action<WebApplication>? pipeline = null)
    {
        var builder = WebApplication.CreateBuilder();
        var logs = new List<LogEntry>();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ListLoggerProvider(logs));

        if (addPackage)
        {
            builder.Services.AddSondorProblemDetails(configure);
        }

        if (mvc)
        {
            builder.Services.AddControllers().AddApplicationPart(typeof(TestApp).Assembly);
        }

        register?.Invoke(builder.Services);

        var app = builder.Build();

        if (usePackage)
        {
            app.UseSondorProblemDetails();
        }

        pipeline?.Invoke(app);

        if (mvc)
        {
            app.MapControllers();
        }

        map?.Invoke(app);

        await app.StartAsync();

        return new TestApp(app, app.GetTestClient(), logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
    }
}

internal static class Json
{
    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(text);

        return document.RootElement.Clone();
    }

    public static string? Text(this JsonElement element, string member) =>
        element.TryGetProperty(member, out var value) ? value.GetString() : null;
}

internal sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception);

internal sealed class ListLoggerProvider(List<LogEntry> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class ListLogger(string category, List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add(new LogEntry(logLevel, category, formatter(state, exception), exception));
            }
        }
    }
}

internal class PaymentDeclinedException(string message) : Exception(message);

internal sealed class CardExpiredException(string message) : PaymentDeclinedException(message);

#pragma warning disable CA1822 // Controller actions are instance methods whether or not they use the instance.

/// <summary>An API controller: answered with problem responses.</summary>
[ApiController]
[Route("api")]
public sealed class ApiTestController : ControllerBase
{
    public sealed class Item
    {
        [Required]
        public string? Name { get; set; }
    }

    [HttpGet("ok")]
    public IResult Ok_() => Result.Success(5).ToHttpResult();

    [HttpGet("missing")]
    public IResult Missing() => Result.Failure<int>(Error.NotFound("Order", "Id")).ToHttpResult();

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("secret connection string");

    [HttpPost("item")]
    public IActionResult Create(Item item) => Ok(item);
}

/// <summary>A controller that is not an API controller: the kind that renders a page.</summary>
public sealed class PageTestController : Controller
{
    [HttpGet("page/throw")]
    public IActionResult Throw() => throw new InvalidOperationException("page failure");

    [HttpGet("page/missing")]
    public IActionResult Missing() => NotFound();
}

/// <summary>An endpoint body that throws, typed so a minimal API accepts it.</summary>
internal static class Fail
{
    public static IResult With(Exception exception) => throw exception;
}
