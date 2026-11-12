namespace Sondor.Cqrs;

/// <summary>
/// The names under which <c>Sondor.Cqrs</c> reports its traces and metrics, for the OpenTelemetry setup:
/// <c>.AddSource(CqrsTelemetry.SourceName)</c> and <c>.AddMeter(CqrsTelemetry.MeterName)</c>.
/// </summary>
public static class CqrsTelemetry
{
    /// <summary>The <see cref="System.Diagnostics.ActivitySource"/> name, used by <see cref="CqrsBuilder.UseTracing"/>.</summary>
    public const string SourceName = "Sondor.Cqrs";

    /// <summary>The <see cref="System.Diagnostics.Metrics.Meter"/> name, used by <see cref="CqrsBuilder.UseMetrics"/>.</summary>
    public const string MeterName = "Sondor.Cqrs";
}
