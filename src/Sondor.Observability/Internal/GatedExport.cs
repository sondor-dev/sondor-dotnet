using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace Sondor.Observability.Internal;

// The gates sit in front of the exporter, so what a signal's mode drops never leaves the process. They derive from the SDK's own
// batch processors and OTLP exporter, so the SDK still hands them the resource and manages their lifetime.

/// <summary>Batches spans for export, leaving out the ones the live settings say to drop.</summary>
internal sealed class GatedActivityProcessor(BaseExporter<Activity> exporter, LiveSettings settings) :
    BatchActivityExportProcessor(exporter, 2048, 5000, 30000, 512)
{
    public override void OnEnd(Activity data)
    {
        var current = settings.Current;

        var keep = current.Traces switch
        {
            SignalMode.Off => false,
            SignalMode.ErrorsOnly => data.Status == ActivityStatusCode.Error,
            _ => true,
        };

        if (keep && !current.IsExcluded(data.Source.Name))
        {
            base.OnEnd(data);
        }
    }
}

/// <summary>Batches log records for export, leaving out the ones the live settings say to drop.</summary>
internal sealed class GatedLogProcessor(BaseExporter<LogRecord> exporter, LiveSettings settings) :
    BatchLogRecordExportProcessor(exporter, 2048, 5000, 30000, 512)
{
    public override void OnEnd(LogRecord data)
    {
        var keep = settings.Current.Logs switch
        {
            SignalMode.Off => false,
            SignalMode.ErrorsOnly => data.LogLevel >= LogLevel.Error,
            _ => true,
        };

        if (keep)
        {
            base.OnEnd(data);
        }
    }
}

/// <summary>The OTLP metric exporter that exports nothing while the live settings say metrics are off.</summary>
[ExportModes(ExportModes.Push)]
internal sealed class GatedMetricExporter(OtlpExporterOptions options, LiveSettings settings) : OtlpMetricExporter(options)
{
    public override ExportResult Export(in Batch<Metric> batch) =>
        settings.Current.Metrics == SignalMode.Off ? ExportResult.Success : base.Export(in batch);
}
