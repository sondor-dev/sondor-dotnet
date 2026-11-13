using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Sondor.Observability.Internal;
using Sondor.Options;

namespace Sondor.Observability;

/// <summary>Registers OpenTelemetry for a service: traces, metrics and logs, configured from one section.</summary>
public static class ObservabilityExtensions
{
    /// <summary>The configuration section read unless another is named.</summary>
    public const string DefaultSection = "Observability";

    /// <summary>
    /// Registers tracing, metrics and logging with one consistent resource. Export to an OTLP collector switches on when an
    /// endpoint is configured; signal modes, the sampling ratio and excluded sources can change while the service runs.
    /// Nothing is instrumented: add what you need through <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The services. The configuration has to be registered, as every host does.</param>
    /// <param name="serviceName">The name the service reports itself as. Required, so telemetry is never anonymous.</param>
    /// <param name="configure">Optional code configuration: version, environment, instance id, sources, meters, instrumentation.</param>
    /// <param name="section">The configuration section. <c>Observability</c> unless set.</param>
    /// <returns>The services.</returns>
    /// <remarks>
    /// The section is optional: without it everything is on at a 10% sampling ratio, and nothing is exported until an endpoint
    /// is configured (here or by <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>). An invalid change while running is rejected and the last
    /// valid settings stay in use, as <c>Sondor.Options</c> does. Exporters you add yourself through the builder are subject to
    /// the sampler, but not to the modes that filter at export (<see cref="SignalMode.ErrorsOnly"/>, excluded sources).
    /// </remarks>
    public static IServiceCollection AddSondorObservability(
        this IServiceCollection services,
        string serviceName,
        Action<ObservabilityBuilder>? configure = null,
        string section = DefaultSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var builder = new ObservabilityBuilder(services, serviceName);
        configure?.Invoke(builder);

        services.AddSondorOptions<ObservabilityOptions>(section, SectionRequirement.Optional);

        services.TryAddSingleton(Ambient.Process);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ObservabilityOptions>, OptionsValidator>());
        services.TryAddSingleton<LiveSettings>();
        services.TryAddSingleton(provider => new ResourceIdentity(
            builder,
            provider.GetRequiredService<IOptionsMonitor<ObservabilityOptions>>(),
            provider.GetService<IHostEnvironment>()));
        services.TryAddSingleton(provider => new OtlpExporters(
            ResolveOtlp(provider),
            provider.GetRequiredService<LiveSettings>()));

        builder.OpenTelemetry.WithTracing().WithMetrics();

        services.ConfigureOpenTelemetryTracerProvider((provider, tracing) =>
        {
            tracing
                .ConfigureResource(provider.GetRequiredService<ResourceIdentity>().Apply)
                .AddSource([.. builder.Sources])
                .SetSampler(new LiveSampler(provider.GetRequiredService<LiveSettings>()));

            if (provider.GetRequiredService<OtlpExporters>().Traces() is { } processor)
            {
                tracing.AddProcessor(processor);
            }
        });

        services.ConfigureOpenTelemetryMeterProvider((provider, metrics) =>
        {
            metrics
                .ConfigureResource(provider.GetRequiredService<ResourceIdentity>().Apply)
                .AddMeter([.. builder.Meters]);

            if (provider.GetRequiredService<OtlpExporters>().Metrics() is { } reader)
            {
                metrics.AddReader(reader);
            }
        });

        builder.OpenTelemetry.WithLogging(
            _ => { },
            logging =>
            {
                logging.IncludeScopes = true;
                logging.IncludeFormattedMessage = true;
            });

        services.ConfigureOpenTelemetryLoggerProvider((provider, logging) =>
        {
            logging.ConfigureResource(provider.GetRequiredService<ResourceIdentity>().Apply);

            if (provider.GetRequiredService<OtlpExporters>().Logs() is { } processor)
            {
                logging.AddProcessor(processor);
            }
        });

        return services;
    }

    private static OtlpSettings? ResolveOtlp(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<ObservabilityOptions>>().CurrentValue;

        // Validated at startup, so any problem was already reported there.
        return OtlpSettings.Resolve(options.Otlp, provider.GetRequiredService<Ambient>(), []);
    }
}
