using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sondor.AspNetCore.Tests;

/// <summary>Calls the exception handler on a bare request, for the cases a real request cannot easily produce.</summary>
internal sealed class HandlerHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private HandlerHost(ServiceProvider provider, List<LogEntry> logs)
    {
        _provider = provider;
        Logs = logs;
    }

    public List<LogEntry> Logs { get; }

    public static HandlerHost Create(Action<ErrorResponseOptions>? configure = null, Action<IServiceCollection>? register = null)
    {
        var logs = new List<LogEntry>();
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new ListLoggerProvider(logs));
        });
        services.AddSondorProblemDetails(configure);
        register?.Invoke(services);

        return new HandlerHost(services.BuildServiceProvider(), logs);
    }

    public async Task<(bool Handled, HttpContext Context)> HandleAsync(Exception exception, CancellationToken aborted = default)
    {
        var scope = _provider.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            RequestAborted = aborted,
        };

        context.Request.Path = "/probe";
        context.Response.Body = new MemoryStream();

        var handler = _provider.GetServices<IExceptionHandler>().Single();
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        return (handled, context);
    }

    public static async Task<string> ReadAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;

        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    public void Dispose() => _provider.Dispose();
}
