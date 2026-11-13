using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sondor.Observability.Benchmarks;

/// <summary>A configuration source that can change and announce it, as a configuration store refresh does.</summary>
public sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Set(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

public static class Hosts
{
    /// <summary>A started host with observability registered for one activity source.</summary>
    public static IHost Start(ReloadableConfiguration configuration, string sourceName)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        ((IConfigurationBuilder)builder.Configuration).Add(configuration);
        builder.Services.AddSondorObservability("benchmark-service", observability => observability.AddSource(sourceName));

        var host = builder.Build();
        host.Start();

        return host;
    }
}

public static class Spans
{
    /// <summary>Starts a span and ends it, which is all a traced operation costs the code that runs it.</summary>
    public static bool Run(ActivitySource source)
    {
        using var activity = source.StartActivity("operation");

        return activity is { Recorded: true };
    }
}
