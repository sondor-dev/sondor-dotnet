using System.Diagnostics.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sondor.Observability.Internal;

namespace Sondor.Observability.Tests;

/// <summary>Event sources the tests write to, named like the SDK's own so the listener picks them up.</summary>
[EventSource(Name = "OpenTelemetry-Sondor-Observability-Test")]
internal sealed class SdkEvents : EventSource
{
    public static readonly SdkEvents Log = new();

    [Event(1, Level = EventLevel.Error, Message = "Export failed: {0}")]
    public void Failed(string reason) => WriteEvent(1, reason);

    [Event(2, Level = EventLevel.Warning, Message = "Slow export: {0}")]
    public void Slow(string detail) => WriteEvent(2, detail);

    [Event(3, Level = EventLevel.Verbose, Message = "Detail: {0}")]
    public void Detail(string detail) => WriteEvent(3, detail);

    [Event(4, Level = EventLevel.Error, Message = "Broken template {1}")]
    public void Broken(string value) => WriteEvent(4, value);

    [Event(5, Level = EventLevel.Informational)]
    public void Unworded(string value) => WriteEvent(5, value);

    [Event(6, Level = EventLevel.Critical, Message = "Critical: {0}")]
    public void Critical(string value) => WriteEvent(6, value);
}

[EventSource(Name = "OpenTelemetry-Sondor-Observability-Late")]
internal sealed class LateSdkEvents : EventSource
{
    [Event(1, Level = EventLevel.Error, Message = "Late failure: {0}")]
    public void Failed(string reason) => WriteEvent(1, reason);
}

[EventSource(Name = "Unrelated-Sondor-Observability-Test")]
internal sealed class UnrelatedEvents : EventSource
{
    public static readonly UnrelatedEvents Log = new();

    [Event(1, Level = EventLevel.Error, Message = "Not ours: {0}")]
    public void Failed(string reason) => WriteEvent(1, reason);
}

/// <summary>Remembers what was logged, with the category it was logged under.</summary>
internal sealed class RecordingLogs : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IEnumerable<string> From(string category) => Entries.Where(entry => entry.Category == category).Select(entry => entry.Message);

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Sink(RecordingLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries)
            {
                owner._entries.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}

/// <summary>Working out why telemetry does not arrive: the SDK's own diagnostics, the startup summary, and reload messages.</summary>
public class DiagnosticsTests
{
    private const string Diagnostics = "Sondor.Observability.Diagnostics";
    private const string Summary = "Sondor.Observability";

    private static T[] Expect<T>(params T[] values) => values;

    private static Task<Running> Start(RecordingLogs logs, ReloadableConfiguration configuration, Action<ObservabilityBuilder>? configure = null) =>
        Running.Start(configuration, configure, services: services => services.AddLogging(logging => logging.AddProvider(logs)));

    [Test]
    public async Task Failures_are_logged_by_default_and_detail_is_not()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(logs, new ReloadableConfiguration().With());

        SdkEvents.Log.Failed("connection refused");
        SdkEvents.Log.Slow("took 9s");
        SdkEvents.Log.Detail("batch of 12");

        Assert.That(logs.From(Diagnostics), Is.EqualTo(Expect(
            "OpenTelemetry-Sondor-Observability-Test: Export failed: connection refused",
            "OpenTelemetry-Sondor-Observability-Test: Slow export: took 9s"
        )));
        Assert.That(logs.Entries.Where(entry => entry.Category == Diagnostics).Select(entry => entry.Level), Is.EqualTo(Expect(LogLevel.Error, LogLevel.Warning)));
    }

    [Test]
    public async Task Debug_shows_everything_the_sdk_reports()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(logs, new ReloadableConfiguration().With(("Observability:Diagnostics:Level", "Debug")));

        SdkEvents.Log.Detail("batch of 12");
        SdkEvents.Log.Critical("out of memory");
        SdkEvents.Log.Unworded("plain");
        SdkEvents.Log.Broken("value");

        Assert.That(logs.From(Diagnostics), Is.EquivalentTo(Expect(
            "OpenTelemetry-Sondor-Observability-Test: Detail: batch of 12",
            "OpenTelemetry-Sondor-Observability-Test: Critical: out of memory",
            "OpenTelemetry-Sondor-Observability-Test: Unworded",
            "OpenTelemetry-Sondor-Observability-Test: Broken template {1}"
        )));
    }

    [Test]
    public async Task Information_sits_between_the_two()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(logs, new ReloadableConfiguration().With(("Observability:Diagnostics:Level", "Information")));

        SdkEvents.Log.Unworded("plain");
        SdkEvents.Log.Detail("not at this level");

        Assert.That(logs.From(Diagnostics), Is.EqualTo(Expect("OpenTelemetry-Sondor-Observability-Test: Unworded")));
    }

    [Test]
    public async Task Error_level_leaves_warnings_out()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(logs, new ReloadableConfiguration().With(("Observability:Diagnostics:Level", "Error")));

        SdkEvents.Log.Slow("a warning");
        SdkEvents.Log.Failed("an error");

        Assert.That(logs.From(Diagnostics), Is.EqualTo(Expect("OpenTelemetry-Sondor-Observability-Test: Export failed: an error")));
    }

    [Test]
    public async Task The_level_follows_a_reload_without_a_restart()
    {
        var logs = new RecordingLogs();
        var configuration = new ReloadableConfiguration().With(("Observability:Diagnostics:Level", "None"));
        await using var running = await Start(logs, configuration);

        SdkEvents.Log.Failed("while off");
        configuration.Replace(("Observability:Diagnostics:Level", "Debug"));
        SdkEvents.Log.Detail("while on");
        configuration.Replace(("Observability:Diagnostics:Level", "None"));
        SdkEvents.Log.Failed("off again");

        Assert.That(logs.From(Diagnostics), Is.EqualTo(Expect("OpenTelemetry-Sondor-Observability-Test: Detail: while on")));
    }

    [Test]
    public async Task An_event_source_created_after_start_up_is_picked_up()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(logs, new ReloadableConfiguration().With());

        using var late = new LateSdkEvents();
        late.Failed("late");
        UnrelatedEvents.Log.Failed("not ours");

        Assert.That(logs.From(Diagnostics), Is.EqualTo(Expect("OpenTelemetry-Sondor-Observability-Late: Late failure: late")));
    }

    [Test]
    public async Task The_sdks_diagnostics_never_reach_the_otlp_log_exporter()
    {
        using var receiver = new OtlpReceiver();
        var configuration = new ReloadableConfiguration().With(
            ("Observability:Otlp:Endpoint", receiver.Endpoint.ToString()),
            ("Observability:Otlp:Protocol", "HttpProtobuf"));

        await using var running = await Running.Start(configuration);

        SdkEvents.Log.Failed("diagnostic-text");
        running.Error("ordinary-text");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/logs", "ordinary-text"), Is.True);
            Assert.That(receiver.Received("/v1/logs", "diagnostic-text"), Is.False, "else a failing export would log about itself for ever");
        });
    }

    [Test]
    public async Task A_real_failed_export_is_visible_in_the_logs()
    {
        var logs = new RecordingLogs();
        var sourceName = Names.Unique("diagnostics.real");
        using var source = new System.Diagnostics.ActivitySource(sourceName);

        await using var running = await Start(
            logs,
            new ReloadableConfiguration().With(
                ("Observability:Otlp:Endpoint", "http://localhost:1"),
                ("Observability:Otlp:Protocol", "HttpProtobuf"),
                ("Observability:Otlp:Timeout", "00:00:02"),
                ("Observability:Traces:SamplingRatio", "1")),
            builder => builder.AddSource(sourceName));

        Sources.Span(source, "doomed");
        running.Flush();

        Assert.That(logs.From(Diagnostics), Has.Some.Contains("OpenTelemetry-Exporter-OpenTelemetryProtocol"));
    }

    [Test]
    public async Task The_start_up_summary_says_what_was_set_up()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(
            logs,
            new ReloadableConfiguration().With(
                ("Observability:Traces:SamplingRatio", "0.25"),
                ("Observability:Metrics:Mode", "Off"),
                ("Observability:Logs:Mode", "ErrorsOnly")),
            builder => builder.AddSource("Orders.Domain", "Payments").AddMeter("Orders.Metrics"));

        var summary = logs.From(Summary).Single();

        Assert.That(
            summary,
            Does.Contain("test-service")
                .And.Contain("traces On (sampling ratio 0.25)")
                .And.Contain("metrics Off")
                .And.Contain("logs ErrorsOnly")
                .And.Contain("not configured, nothing is exported")
                .And.Contain("sources: Orders.Domain, Payments")
                .And.Contain("meters: Orders.Metrics")
                .And.Contain("SDK diagnostics at Warning"));
    }

    [Test]
    public async Task The_summary_names_the_endpoint_without_credentials_or_headers()
    {
        var logs = new RecordingLogs();
        await using var running = await Start(
            logs,
            new ReloadableConfiguration().With(
                ("Observability:Otlp:Endpoint", "https://user:hunter2@collector.example:4317/path?token=abc"),
                ("Observability:Otlp:Headers", "x-api-key=secret-value")));

        var everything = string.Join('\n', logs.Entries.Select(entry => entry.Message));

        Assert.Multiple(() =>
        {
            Assert.That(logs.From(Summary).Single(), Does.Contain("https://collector.example:4317 (Grpc)").And.Contain("sources: (none)"));
            Assert.That(everything, Does.Not.Contain("hunter2").And.Not.Contain("secret-value").And.Not.Contain("token=abc"));
        });
    }

    [Test]
    public async Task A_change_to_the_live_settings_is_logged_and_an_unrelated_one_is_not()
    {
        var logs = new RecordingLogs();
        var configuration = new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "1"));
        await using var running = await Start(logs, configuration);

        configuration.Replace(("Observability:Traces:SamplingRatio", "1"), ("Observability:Version", "9.9.9"));
        var afterUnrelated = logs.From(Summary).Count();

        configuration.Replace(("Observability:Traces:SamplingRatio", "0.01"), ("Observability:Traces:ExcludeSources:0", "Noisy"));

        var messages = logs.From(Summary).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(afterUnrelated, Is.EqualTo(1), "only the start-up summary so far");
            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages[1], Does.Contain("settings changed").And.Contain("sampling ratio 0.01").And.Contain("excluded sources: Noisy"));
        });
    }

    [Test]
    public void Levels_map_both_ways()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Critical), Is.EqualTo(EventLevel.Error));
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Error), Is.EqualTo(EventLevel.Error));
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Warning), Is.EqualTo(EventLevel.Warning));
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Information), Is.EqualTo(EventLevel.Informational));
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Debug), Is.EqualTo(EventLevel.Verbose));
            Assert.That(SdkDiagnostics.ToEventLevel(LogLevel.Trace), Is.EqualTo(EventLevel.Verbose));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.LogAlways), Is.EqualTo(LogLevel.Critical));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.Critical), Is.EqualTo(LogLevel.Critical));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.Error), Is.EqualTo(LogLevel.Error));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.Warning), Is.EqualTo(LogLevel.Warning));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.Informational), Is.EqualTo(LogLevel.Information));
            Assert.That(SdkDiagnostics.ToLogLevel(EventLevel.Verbose), Is.EqualTo(LogLevel.Debug));
        });
    }

    [Test]
    public async Task An_unknown_diagnostics_level_stops_the_host()
    {
        Exception? caught = null;

        try
        {
            await Running.Start(new ReloadableConfiguration().With(("Observability:Diagnostics:Level", "42")));
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        Assert.That(caught, Is.Not.Null.And.Message.Contains("Diagnostics:Level"));
    }
}
