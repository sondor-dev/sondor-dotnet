using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Sondor.Observability;

/// <summary>
/// Configures <c>AddSondorObservability</c> in code. What it sets wins over configuration, which wins over the defaults.
/// Nothing is instrumented unless you ask: add instrumentation packages through <see cref="WithTracing"/> and
/// <see cref="WithMetrics"/>, or anything else the OpenTelemetry SDK offers through <see cref="OpenTelemetry"/>.
/// </summary>
public sealed class ObservabilityBuilder
{
    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private readonly List<string> _sources = [];
    private readonly List<string> _meters = [];

    internal ObservabilityBuilder(IServiceCollection services, string serviceName)
    {
        Services = services;
        ServiceName = serviceName;
        OpenTelemetry = services.AddOpenTelemetry();
    }

    /// <summary>The services.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The service name that was given.</summary>
    public string ServiceName { get; }

    /// <summary>The OpenTelemetry SDK's own builder, for anything this one does not cover: an exporter, a processor, a view.</summary>
    public OpenTelemetryBuilder OpenTelemetry { get; }

    internal string? Version { get; private set; }

    internal string? EnvironmentName { get; private set; }

    internal string? InstanceId { get; private set; }

    internal IReadOnlyDictionary<string, string> Attributes => _attributes;

    internal IReadOnlyList<string> Sources => _sources;

    internal IReadOnlyList<string> Meters => _meters;

    /// <summary>Sets the service version, instead of the configured one or the entry assembly's.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithVersion(string version) => Set(value => Version = value, version, nameof(version));

    /// <summary>Sets the deployment environment, instead of the configured one or the host's.</summary>
    /// <param name="environment">The environment, for example <c>Production</c>.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithEnvironment(string environment) => Set(value => EnvironmentName = value, environment, nameof(environment));

    /// <summary>Sets the instance id, instead of the configured one or a new GUID.</summary>
    /// <param name="instanceId">An id that is unique to this running instance.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithInstanceId(string instanceId) => Set(value => InstanceId = value, instanceId, nameof(instanceId));

    /// <summary>Adds a resource attribute, or replaces the configured one of that key.</summary>
    /// <param name="key">The attribute key, for example <c>team</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithResourceAttribute(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        _attributes[key] = value;

        return this;
    }

    /// <summary>Listens to activity sources by name, for example <c>"Sondor.Cqrs"</c>, or your own.</summary>
    /// <param name="names">The source names. A trailing <c>*</c> is a wildcard.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder AddSource(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        _sources.AddRange(names);

        return this;
    }

    /// <summary>Listens to meters by name, for example <c>"Sondor.Cqrs"</c>, or your own.</summary>
    /// <param name="names">The meter names. A trailing <c>*</c> is a wildcard.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder AddMeter(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        _meters.AddRange(names);

        return this;
    }

    /// <summary>Configures tracing: instrumentation, processors, another exporter.</summary>
    /// <param name="configure">The configuration.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithTracing(Action<TracerProviderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        OpenTelemetry.WithTracing(configure);

        return this;
    }

    /// <summary>Configures metrics: instrumentation, readers, another exporter.</summary>
    /// <param name="configure">The configuration.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithMetrics(Action<MeterProviderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        OpenTelemetry.WithMetrics(configure);

        return this;
    }

    /// <summary>Configures logging: processors, another exporter.</summary>
    /// <param name="configure">The configuration.</param>
    /// <returns>The builder.</returns>
    public ObservabilityBuilder WithLogging(Action<LoggerProviderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        OpenTelemetry.WithLogging(configure);

        return this;
    }

    private ObservabilityBuilder Set(Action<string> assign, string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);

        assign(value);

        return this;
    }
}
