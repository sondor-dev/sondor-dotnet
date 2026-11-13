using OpenTelemetry.Exporter;

namespace Sondor.Observability.Internal;

/// <summary>Builds the OTLP exporters, or none when no endpoint is configured.</summary>
internal sealed class OtlpExporters(OtlpSettings? settings, LiveSettings live)
{
    private const int MetricExportIntervalMilliseconds = 60_000;
    private const int MetricExportTimeoutMilliseconds = 30_000;

    public OtlpSettings? Settings => settings;

    public GatedActivityProcessor? Traces() =>
        settings is null ? null : new GatedActivityProcessor(new OtlpTraceExporter(Options("traces")), live);

    public GatedLogProcessor? Logs() =>
        settings is null ? null : new GatedLogProcessor(new OtlpLogExporter(Options("logs")), live);

    public OpenTelemetry.Metrics.PeriodicExportingMetricReader? Metrics() =>
        settings is null
            ? null
            : new OpenTelemetry.Metrics.PeriodicExportingMetricReader(
                new GatedMetricExporter(Options("metrics"), live),
                MetricExportIntervalMilliseconds,
                MetricExportTimeoutMilliseconds);

    private OtlpExporterOptions Options(string signal)
    {
        var resolved = settings!;

        return new OtlpExporterOptions
        {
            Endpoint = resolved.EndpointFor(signal),
            Protocol = resolved.Protocol == OtlpProtocol.HttpProtobuf ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc,
            Headers = resolved.Headers,
            TimeoutMilliseconds = (int)resolved.Timeout.TotalMilliseconds,
        };
    }
}
