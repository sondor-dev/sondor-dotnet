using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sondor.Options.FluentValidation.Tests;

/// <summary>A configuration source that can change and announce it, the way a refresh from Azure App Configuration does.</summary>
internal sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public ReloadableConfiguration With(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        return this;
    }

    public void Replace(params (string Key, string? Value)[] values)
    {
        With(values);
        OnReload();
    }
}

internal sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int Timeout { get; set; } = 30;

    public PoolOptions Pool { get; set; } = new();
}

internal sealed class PoolOptions
{
    public int Size { get; set; } = 10;
}

internal sealed class DatabaseOptionsValidator : AbstractValidator<DatabaseOptions>
{
    public DatabaseOptionsValidator()
    {
        RuleFor(options => options.ConnectionString).NotEmpty().WithMessage("A connection string is required.");
        RuleFor(options => options.Timeout).InclusiveBetween(1, 300).WithMessage("The timeout must be 1 to 300 seconds.");
        RuleFor(options => options.Pool.Size).GreaterThan(0).WithMessage("The pool needs at least one connection.");
    }
}

/// <summary>A scoped service, to show that a validator can depend on one and that each validation has a scope of its own.</summary>
internal sealed class ScopeMarker;

internal sealed class ScopedDependencyValidator : AbstractValidator<DatabaseOptions>
{
    internal static readonly System.Collections.Concurrent.ConcurrentBag<ScopeMarker> Seen = [];

    public ScopedDependencyValidator(ScopeMarker marker) => Seen.Add(marker);
}

internal sealed class CountingValidator : AbstractValidator<DatabaseOptions>
{
    internal static int Runs;

    public CountingValidator() =>
        RuleFor(options => options).Must(_ =>
        {
            Interlocked.Increment(ref Runs);

            return true;
        });
}

internal sealed class AsyncValidator : AbstractValidator<DatabaseOptions>
{
    public AsyncValidator() =>
        RuleFor(options => options.ConnectionString).MustAsync((_, _) => Task.FromResult(true));
}

internal sealed class ModelLevelValidator : AbstractValidator<DatabaseOptions>
{
    public ModelLevelValidator() =>
        RuleFor(options => options).Must(_ => false).WithMessage("The whole thing is wrong.");
}

internal static class Hosting
{
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
