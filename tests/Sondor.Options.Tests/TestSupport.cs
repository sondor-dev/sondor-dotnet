using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sondor.Options.Tests;

/// <summary>A configuration source that can change and announce it, the way a refresh from Azure App Configuration does.</summary>
internal sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    /// <summary>Replaces everything this source holds and announces a reload, as a refresh does when the sentinel key changes.</summary>
    public void Replace(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }

    public ReloadableConfiguration With(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return this;
    }
}

internal sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int Timeout { get; set; } = 30;
}

internal sealed class FeatureOptions
{
    public bool Enabled { get; set; }

    public string Endpoint { get; set; } = string.Empty;
}

internal sealed record ImmutableOptions
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];
}

internal static class Hosting
{
    /// <summary>A host with nothing but the given configuration, so a test sees only what it set up.</summary>
    internal static HostApplicationBuilder Builder(ReloadableConfiguration configuration)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        ((IConfigurationBuilder)builder.Configuration).Add(configuration);

        return builder;
    }

    internal static ServiceProvider Provider(
        ReloadableConfiguration configuration,
        Action<IServiceCollection> register,
        CapturedLogs? logs = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Add(configuration).Build());

        if (logs is not null)
        {
            services.AddLogging(logging => logging.AddProvider(logs));
        }

        register(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>Keeps what is logged, so a test can say what an operator would have seen.</summary>
internal sealed class CapturedLogs : ILoggerProvider, ILogger
{
    internal List<(LogLevel Level, string Message)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));

    public void Dispose()
    {
    }
}
