using BenchmarkDotNet.Attributes;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sondor.Options.FluentValidation.Benchmarks;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int Timeout { get; set; } = 30;

    public int Retries { get; set; } = 3;
}

public sealed class DatabaseOptionsValidator : AbstractValidator<DatabaseOptions>
{
    public DatabaseOptionsValidator()
    {
        RuleFor(options => options.ConnectionString).NotEmpty();
        RuleFor(options => options.Timeout).InclusiveBetween(1, 300);
        RuleFor(options => options.Retries).InclusiveBetween(0, 10);
    }
}

public sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Set(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

/// <summary>
/// Validating with FluentValidation against the same three checks written as a delegate. A validation resolves the validator
/// from a scope of its own, so the question is what that costs, and that it does not grow with the changes already seen:
/// every reload case first goes through <see cref="PriorReloads"/> changes.
/// </summary>
[MemoryDiagnoser]
public class ValidationBenchmarks
{
    private ReloadableConfiguration _delegateSource = null!;
    private ReloadableConfiguration _fluentSource = null!;
    private ServiceProvider _delegateProvider = null!;
    private ServiceProvider _fluentProvider = null!;
    private IOptionsMonitor<DatabaseOptions> _delegateMonitor = null!;
    private IOptionsMonitor<DatabaseOptions> _fluentMonitor = null!;
    private int _next;

    [Params(0, 10_000)]
    public int PriorReloads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _delegateSource = new ReloadableConfiguration();
        _fluentSource = new ReloadableConfiguration();
        _delegateSource.Set(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "1"));
        _fluentSource.Set(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "1"));

        _delegateProvider = Build(
            _delegateSource,
            services => services.AddSondorOptions<DatabaseOptions>().Validate(
                options => options.ConnectionString.Length > 0 && options.Timeout is >= 1 and <= 300 && options.Retries is >= 0 and <= 10));
        _fluentProvider = Build(
            _fluentSource,
            services => services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>());

        _delegateMonitor = _delegateProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _fluentMonitor = _fluentProvider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _delegateMonitor.OnChange(_ => { });
        _fluentMonitor.OnChange(_ => { });

        for (var reload = 0; reload < PriorReloads; reload++)
        {
            Apply(_delegateSource, _delegateMonitor);
            Apply(_fluentSource, _fluentMonitor);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _delegateProvider.Dispose();
        _fluentProvider.Dispose();
    }

    private static ServiceProvider Build(ReloadableConfiguration source, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Add(source).Build());
        register(services);

        return services.BuildServiceProvider();
    }

    private int Apply(ReloadableConfiguration source, IOptionsMonitor<DatabaseOptions> monitor)
    {
        source.Set(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", (++_next % 250 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return monitor.CurrentValue.Timeout;
    }

    [Benchmark(Baseline = true)]
    public int DelegateValidChange() => Apply(_delegateSource, _delegateMonitor);

    [Benchmark]
    public int FluentValidationValidChange() => Apply(_fluentSource, _fluentMonitor);

    [Benchmark]
    public int FluentValidationSnapshotPerScope()
    {
        using var scope = _fluentProvider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout;
    }

    [Benchmark]
    public int DelegateSnapshotPerScope()
    {
        using var scope = _delegateProvider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout;
    }
}
