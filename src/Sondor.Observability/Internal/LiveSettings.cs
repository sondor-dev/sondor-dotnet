using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace Sondor.Observability.Internal;

/// <summary>
/// The settings that change while the service runs, read by every span, log and export. A reload builds one new
/// <see cref="Snapshot"/> and swaps it in, so a reader never sees half a change and never pays for the options system.
/// </summary>
internal sealed class LiveSettings : IDisposable
{
    private readonly IDisposable? _subscription;
    private volatile Snapshot _current;

    public LiveSettings(IOptionsMonitor<ObservabilityOptions> options)
    {
        _current = new Snapshot(options.CurrentValue);
        _subscription = options.OnChange(changed => _current = new Snapshot(changed));
    }

    /// <summary>The settings in force right now.</summary>
    public Snapshot Current => _current;

    public void Dispose() => _subscription?.Dispose();

    /// <summary>One consistent set of live settings. Immutable.</summary>
    internal sealed class Snapshot
    {
        private readonly HashSet<string> _excludedSources;

        public Snapshot(ObservabilityOptions options)
        {
            Traces = options.Traces.Mode;
            Metrics = options.Metrics.Mode;
            Logs = options.Logs.Mode;
            _excludedSources = new HashSet<string>(options.Traces.ExcludeSources, StringComparer.Ordinal);

            Sampler = Traces switch
            {
                SignalMode.Off => new AlwaysOffSampler(),
                // Every span has to be recorded to find the ones that fail; the export keeps only those.
                SignalMode.ErrorsOnly => new AlwaysOnSampler(),
                _ => new ParentBasedSampler(new TraceIdRatioBasedSampler(options.Traces.SamplingRatio)),
            };
        }

        public SignalMode Traces { get; }

        public SignalMode Metrics { get; }

        public SignalMode Logs { get; }

        public Sampler Sampler { get; }

        public bool IsExcluded(string sourceName) => _excludedSources.Count > 0 && _excludedSources.Contains(sourceName);
    }
}
