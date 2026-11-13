namespace Sondor.Observability;

/// <summary>What a signal does with the telemetry it produces.</summary>
/// <remarks>The default is <see cref="On"/>, so a signal that is not configured keeps working.</remarks>
public enum SignalMode
{
    /// <summary>The signal is exported, subject to its own settings (for traces, the sampling ratio).</summary>
    On = 0,

    /// <summary>Only errors are exported: logs at Error and above, spans that ended with an error status. Not available for metrics.</summary>
    ErrorsOnly = 1,

    /// <summary>Nothing is exported.</summary>
    Off = 2,
}

/// <summary>The protocol OTLP export uses.</summary>
public enum OtlpProtocol
{
    /// <summary>gRPC, usually port 4317. The OpenTelemetry default.</summary>
    Grpc = 0,

    /// <summary>HTTP with protobuf bodies, usually port 4318. The signal path (<c>/v1/traces</c>, ...) is added for you.</summary>
    HttpProtobuf = 1,
}

/// <summary>
/// The settings of <c>AddSondorObservability</c>, read from the <c>Observability</c> section unless another is named. Everything
/// is optional except the service name, which is a parameter. Modes, the sampling ratio and the excluded sources follow a
/// configuration reload; everything else is read once at startup.
/// </summary>
public sealed class ObservabilityOptions
{
    /// <summary>The service version. Defaults to the entry assembly's version. Read at startup.</summary>
    public string? Version { get; init; }

    /// <summary>The deployment environment, for example <c>Production</c>. Defaults to the host's environment name. Read at startup.</summary>
    public string? Environment { get; init; }

    /// <summary>Identifies this running instance. Defaults to a new GUID for each process. Read at startup.</summary>
    public string? InstanceId { get; init; }

    /// <summary>Extra resource attributes, by their OpenTelemetry key, for example <c>team</c>. Read at startup.</summary>
    public Dictionary<string, string> ResourceAttributes { get; init; } = [];

    /// <summary>Traces.</summary>
    public TracesOptions Traces { get; init; } = new();

    /// <summary>Metrics. <see cref="SignalMode.ErrorsOnly"/> is not valid for metrics.</summary>
    public SignalOptions Metrics { get; init; } = new();

    /// <summary>Logs.</summary>
    public SignalOptions Logs { get; init; } = new();

    /// <summary>OTLP export. It switches on when an endpoint is configured here or by the standard <c>OTEL_EXPORTER_OTLP_*</c> variables.</summary>
    public OtlpOptions Otlp { get; init; } = new();
}

/// <summary>The settings every signal has.</summary>
public class SignalOptions
{
    /// <summary>What the signal does with its telemetry. Reloads live.</summary>
    public SignalMode Mode { get; init; } = SignalMode.On;
}

/// <summary>Trace settings.</summary>
public sealed class TracesOptions : SignalOptions
{
    /// <summary>
    /// The share of new traces that is kept, from 0 to 1, while the mode is <see cref="SignalMode.On"/>. A trace that
    /// arrives with its parent's decision follows that decision, so a trace is never half sampled. 0.1 unless set. Reloads live.
    /// </summary>
    public double SamplingRatio { get; init; } = 0.1;

    /// <summary>Names of activity sources whose spans are not exported, for noisy sources. Reloads live.</summary>
    public string[] ExcludeSources { get; init; } = [];
}

/// <summary>OTLP export settings. Read once at startup.</summary>
/// <remarks>
/// Whatever is not set here falls back to the standard variables: <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>,
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> (<c>grpc</c> or <c>http/protobuf</c>), <c>OTEL_EXPORTER_OTLP_HEADERS</c> and
/// <c>OTEL_EXPORTER_OTLP_TIMEOUT</c> (milliseconds).
/// </remarks>
public sealed class OtlpOptions
{
    /// <summary>The collector's address, for example <c>http://collector:4317</c>. With no endpoint nothing is exported.</summary>
    public string? Endpoint { get; init; }

    /// <summary>The protocol. gRPC unless set.</summary>
    public OtlpProtocol? Protocol { get; init; }

    /// <summary>
    /// Headers sent with every export, as <c>key=value,key2=value2</c>. The value may instead be the path of a file under
    /// <c>/run/secrets</c> (a Docker secret), which is read at startup. Never logged.
    /// </summary>
    public string? Headers { get; init; }

    /// <summary>The path of a file holding the headers, for example a Docker or Kubernetes secret. Not combined with <see cref="Headers"/>.</summary>
    public string? HeadersFile { get; init; }

    /// <summary>How long one export may take. Ten seconds unless set.</summary>
    public TimeSpan? Timeout { get; init; }
}
