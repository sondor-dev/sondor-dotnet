using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Sondor.Observability.Tests;

/// <summary>A configuration source that can change and announce it, the way a refresh from a configuration store does.</summary>
internal sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public ReloadableConfiguration With(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return this;
    }

    public void Replace(params (string Key, string? Value)[] values)
    {
        With(values);
        OnReload();
    }
}

/// <summary>A running host with observability registered, and the three providers it built.</summary>
internal sealed partial class Running : IAsyncDisposable
{
    private readonly IHost _host;

    private Running(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public TracerProvider Tracer => _host.Services.GetRequiredService<TracerProvider>();

    public MeterProvider Meter => _host.Services.GetRequiredService<MeterProvider>();

    public LoggerProvider Logger => _host.Services.GetRequiredService<LoggerProvider>();

    public void Error(string text) => TestLog.Error(_host.Services.GetRequiredService<ILogger<Running>>(), text);

    public void Warning(string text) => TestLog.Warning(_host.Services.GetRequiredService<ILogger<Running>>(), text);

    public static async Task<Running> Start(
        ReloadableConfiguration configuration,
        Action<ObservabilityBuilder>? configure = null,
        string serviceName = "test-service",
        Action<IServiceCollection>? services = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        ((IConfigurationBuilder)builder.Configuration).Add(configuration);

        builder.Services.AddSondorObservability(serviceName, configure);
        services?.Invoke(builder.Services);

        var host = builder.Build();
        await host.StartAsync();

        return new Running(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    /// <summary>Exports everything that is waiting.</summary>
    public void Flush()
    {
        Tracer.ForceFlush(10_000);
        Meter.ForceFlush(10_000);
        Logger.ForceFlush(10_000);
    }
}

internal static class Names
{
    /// <summary>A name no other test uses, because activity sources and meters are process wide.</summary>
    public static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";
}

/// <summary>An OTLP receiver over HTTP that remembers what it was sent, so a test can see what left the process.</summary>
internal sealed class OtlpReceiver : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<Request> _requests = [];
    private readonly Task _loop;

    public OtlpReceiver()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        Endpoint = new Uri($"http://localhost:{port}");
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _loop = Task.Run(Listen);
    }

    public Uri Endpoint { get; }

    public IReadOnlyList<Request> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<Request> To(string path) => [.. Requests.Where(request => request.Path == path)];

    /// <summary>Whether text was sent on that path. Protobuf writes strings as they are, so a name can be found in the body.</summary>
    public bool Received(string path, string text) => To(path).Any(request => request.Body.Contains(text, StringComparison.Ordinal));

    public void Dispose()
    {
        _listener.Close();

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The listener was closed while it waited.
        }
    }

    private async Task Listen()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            using var buffer = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(buffer);

            lock (_requests)
            {
                _requests.Add(new Request(
                    context.Request.Url!.AbsolutePath,
                    Encoding.UTF8.GetString(buffer.ToArray()),
                    context.Request.Headers.AllKeys.ToDictionary(key => key!, key => context.Request.Headers[key]!, StringComparer.OrdinalIgnoreCase)));
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/x-protobuf";
            context.Response.Close();
        }
    }

    internal sealed record Request(string Path, string Body, IReadOnlyDictionary<string, string> Headers);
}

internal static class Sources
{
    /// <summary>Starts a span and ends it, returning whether it was recorded for export.</summary>
    public static bool Span(ActivitySource source, string name, bool fail = false)
    {
        using var activity = source.StartActivity(name);

        if (fail)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        return activity is { Recorded: true };
    }
}

internal static partial class TestLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "{Text}")]
    public static partial void Error(ILogger logger, string text);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Text}")]
    public static partial void Warning(ILogger logger, string text);
}
