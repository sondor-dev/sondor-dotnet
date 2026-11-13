using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Observability.Internal;

/// <summary>What was registered in code, for the startup summary.</summary>
internal sealed record Registered(string ServiceName, IReadOnlyList<string> Sources, IReadOnlyList<string> Meters);

/// <summary>
/// Makes a missing trace explainable: logs once what was set up, logs each reload of the live settings, and runs the SDK
/// diagnostics listener at the configured level.
/// </summary>
internal sealed partial class DiagnosticsService : IHostedService, IDisposable
{
    private readonly IOptionsMonitor<ObservabilityOptions> _options;
    private readonly Registered _registered;
    private readonly OtlpExporters _exporters;
    private readonly ILogger _logger;
    private readonly ILogger _diagnosticsLogger;
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private SdkDiagnostics? _listener;
    private ObservabilityOptions _current;
    private LogLevel? _applied;

    public DiagnosticsService(
        IOptionsMonitor<ObservabilityOptions> options,
        Registered registered,
        OtlpExporters exporters,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _registered = registered;
        _exporters = exporters;
        _logger = loggerFactory.CreateLogger("Sondor.Observability");
        _diagnosticsLogger = loggerFactory.CreateLogger(DiagnosticsCategory.Name);
        _current = options.CurrentValue;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Apply(_current.Diagnostics.Level);

            var options = _current;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var otlp = Describe(_exporters.Settings);
                var sources = List(_registered.Sources);
                var meters = List(_registered.Meters);

                Started(
                    _logger,
                    _registered.ServiceName,
                    options.Traces.Mode,
                    options.Traces.SamplingRatio,
                    options.Metrics.Mode,
                    options.Logs.Mode,
                    otlp,
                    sources,
                    meters,
                    options.Diagnostics.Level);
            }

            _subscription = _options.OnChange(Changed);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _subscription?.Dispose();
            _subscription = null;
            _listener?.Dispose();
            _listener = null;
        }
    }

    private static string List(IReadOnlyList<string> names) => names.Count == 0 ? "(none)" : string.Join(", ", names);

    /// <summary>Where telemetry goes, without a user name, password or query string that the address might carry.</summary>
    private static string Describe(OtlpSettings? settings) =>
        settings is null
            ? "not configured, nothing is exported"
            : $"{settings.Endpoint.Scheme}://{settings.Endpoint.Authority} ({settings.Protocol})";

    private void Changed(ObservabilityOptions changed)
    {
        lock (_gate)
        {
            var before = _current;
            _current = changed;

            Apply(changed.Diagnostics.Level);

            if (before.Traces.Mode == changed.Traces.Mode &&
                before.Traces.SamplingRatio.Equals(changed.Traces.SamplingRatio) &&
                before.Metrics.Mode == changed.Metrics.Mode &&
                before.Logs.Mode == changed.Logs.Mode &&
                before.Traces.ExcludeSources.SequenceEqual(changed.Traces.ExcludeSources, StringComparer.Ordinal) &&
                before.Diagnostics.Level == changed.Diagnostics.Level)
            {
                return;
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                var excluded = List(changed.Traces.ExcludeSources);

                Reloaded(
                    _logger,
                    changed.Traces.Mode,
                    changed.Traces.SamplingRatio,
                    changed.Metrics.Mode,
                    changed.Logs.Mode,
                    excluded,
                    changed.Diagnostics.Level);
            }
        }
    }

    private void Apply(LogLevel level)
    {
        if (_applied == level)
        {
            return;
        }

        _applied = level;
        _listener?.Dispose();
        _listener = level == LogLevel.None ? null : new SdkDiagnostics(_diagnosticsLogger, level);
    }

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Observability for {Service}: traces {Traces} (sampling ratio {Ratio}), metrics {Metrics}, logs {Logs}; OTLP export {Otlp}; listening to sources: {Sources}; meters: {Meters}; SDK diagnostics at {Diagnostics}.")]
    private static partial void Started(
        ILogger logger,
        string service,
        SignalMode traces,
        double ratio,
        SignalMode metrics,
        SignalMode logs,
        string otlp,
        string sources,
        string meters,
        LogLevel diagnostics);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Information,
        Message = "Observability settings changed: traces {Traces} (sampling ratio {Ratio}), metrics {Metrics}, logs {Logs}; excluded sources: {Excluded}; SDK diagnostics at {Diagnostics}.")]
    private static partial void Reloaded(
        ILogger logger,
        SignalMode traces,
        double ratio,
        SignalMode metrics,
        SignalMode logs,
        string excluded,
        LogLevel diagnostics);
}
