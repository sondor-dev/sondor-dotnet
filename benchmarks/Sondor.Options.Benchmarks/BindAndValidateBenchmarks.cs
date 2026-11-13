using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sondor.Options.Benchmarks;

/// <summary>
/// Registering and building options, and reading them once built, against the framework's own registration. Sondor.Options
/// replaces the options factory, so what has to hold is that reading costs the same and allocates nothing more.
/// </summary>
[MemoryDiagnoser]
public class BindAndValidateBenchmarks
{
    private IConfiguration _configuration = null!;
    private ServiceProvider _framework = null!;
    private ServiceProvider _sondor = null!;
    private ServiceProvider _optionalAbsent = null!;
    private IOptions<DatabaseOptions> _frameworkOptions = null!;
    private IOptions<DatabaseOptions> _sondorOptions = null!;
    private IOptionsMonitor<DatabaseOptions> _frameworkMonitor = null!;
    private IOptionsMonitor<DatabaseOptions> _sondorMonitor = null!;
    private IOptions<DatabaseOptions> _absentOptions = null!;

    [GlobalSetup]
    public void Setup()
    {
        var source = new ReloadableConfiguration();
        source.Set(
            ("DatabaseOptions:ConnectionString", "Host=db;Database=app"),
            ("DatabaseOptions:Timeout", "10"),
            ("DatabaseOptions:Retries", "5"));
        _configuration = Containers.Configuration(source);

        _framework = Containers.Build(_configuration, FrameworkRegistration);
        _sondor = Containers.Build(_configuration, SondorRegistration);
        _optionalAbsent = Containers.Build(
            Containers.Configuration(new ReloadableConfiguration()),
            services => services.AddSondorOptions<DatabaseOptions>(requirement: SectionRequirement.Optional).Validate(_ => true));

        _frameworkOptions = _framework.GetRequiredService<IOptions<DatabaseOptions>>();
        _sondorOptions = _sondor.GetRequiredService<IOptions<DatabaseOptions>>();
        _frameworkMonitor = _framework.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _sondorMonitor = _sondor.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _absentOptions = _optionalAbsent.GetRequiredService<IOptions<DatabaseOptions>>();

        // Everything is built once before measuring, so the reads below are the steady state.
        _ = _frameworkOptions.Value;
        _ = _sondorOptions.Value;
        _ = _frameworkMonitor.CurrentValue;
        _sondorMonitor.CurrentValue.ToString();
        _ = _absentOptions.Value;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _framework.Dispose();
        _sondor.Dispose();
        _optionalAbsent.Dispose();
    }

    private static void FrameworkRegistration(IServiceCollection services) =>
        services.AddOptions<DatabaseOptions>().BindConfiguration("DatabaseOptions").Validate(options => options.Timeout > 0).ValidateOnStart();

    private static void SondorRegistration(IServiceCollection services) =>
        services.AddSondorOptions<DatabaseOptions>().Validate(options => options.Timeout > 0);

    // Registration and the first build: what a service pays once, at startup.

    [Benchmark]
    public void FrameworkRegisterBuildAndRead()
    {
        using var provider = Containers.Build(_configuration, FrameworkRegistration);
        _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }

    [Benchmark]
    public void SondorRegisterBuildAndRead()
    {
        using var provider = Containers.Build(_configuration, SondorRegistration);
        _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    }

    // Reading once built: what every request pays. Both are expected to allocate nothing.

    [Benchmark(Baseline = true)]
    public int FrameworkIOptionsValue() => _frameworkOptions.Value.Timeout;

    [Benchmark]
    public int SondorIOptionsValue() => _sondorOptions.Value.Timeout;

    [Benchmark]
    public int FrameworkIOptionsMonitorCurrentValue() => _frameworkMonitor.CurrentValue.Timeout;

    [Benchmark]
    public int SondorIOptionsMonitorCurrentValue() => _sondorMonitor.CurrentValue.Timeout;

    [Benchmark]
    public int SondorOptionalSectionAbsentIOptionsValue() => _absentOptions.Value.Timeout;

    // IOptionsSnapshot builds the options in every scope, so it exercises the factory on each request.

    [Benchmark]
    public int FrameworkIOptionsSnapshotPerScope()
    {
        using var scope = _framework.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout;
    }

    [Benchmark]
    public int SondorIOptionsSnapshotPerScope()
    {
        using var scope = _sondor.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout;
    }
}
