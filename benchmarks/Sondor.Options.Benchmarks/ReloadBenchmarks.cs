using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sondor.Options.Benchmarks;

/// <summary>
/// One configuration change: the source announces it and the monitor builds the new options. Each case first goes through
/// <see cref="PriorReloads"/> changes, so a cost that grows with the number of changes already seen (the kind of defect the
/// Sondor.Cqrs tracing benchmarks found) shows up as a difference between the two parameter values.
/// </summary>
[MemoryDiagnoser]
public class ReloadBenchmarks
{
    private ReloadableConfiguration _frameworkSource = null!;
    private ReloadableConfiguration _sondorSource = null!;
    private ReloadableConfiguration _rejectedSource = null!;
    private ServiceProvider _framework = null!;
    private ServiceProvider _sondor = null!;
    private ServiceProvider _rejected = null!;
    private IOptionsMonitor<DatabaseOptions> _frameworkMonitor = null!;
    private IOptionsMonitor<DatabaseOptions> _sondorMonitor = null!;
    private IOptionsMonitor<DatabaseOptions> _rejectedMonitor = null!;
    private int _next;

    [Params(0, 10_000)]
    public int PriorReloads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _frameworkSource = new ReloadableConfiguration();
        _sondorSource = new ReloadableConfiguration();
        _rejectedSource = new ReloadableConfiguration();
        _frameworkSource.Set(("DatabaseOptions:Timeout", "1"));
        _sondorSource.Set(("DatabaseOptions:Timeout", "1"));
        _rejectedSource.Set(("DatabaseOptions:Timeout", "1"));

        _framework = Containers.Build(
            Containers.Configuration(_frameworkSource),
            services => services.AddOptions<DatabaseOptions>().BindConfiguration("DatabaseOptions").Validate(options => options.Timeout > 0).ValidateOnStart());
        _sondor = Containers.Build(
            Containers.Configuration(_sondorSource),
            services => services.AddSondorOptions<DatabaseOptions>().Validate(options => options.Timeout > 0));
        _rejected = Containers.Build(
            Containers.Configuration(_rejectedSource),
            services => services.AddSondorOptions<DatabaseOptions>().Validate(options => options.Timeout > 0));

        _frameworkMonitor = _framework.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _sondorMonitor = _sondor.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _rejectedMonitor = _rejected.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();

        // A listener, as a real service has: the cost of telling it is part of a change.
        _frameworkMonitor.OnChange(_ => { });
        _sondorMonitor.OnChange(_ => { });
        _rejectedMonitor.OnChange(_ => { });

        // Startup is strict: there has to be a valid value before a bad change can be rejected in favour of it.
        _ = _rejectedMonitor.CurrentValue;

        for (var reload = 0; reload < PriorReloads; reload++)
        {
            Apply(_frameworkSource, _frameworkMonitor);
            Apply(_sondorSource, _sondorMonitor);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _framework.Dispose();
        _sondor.Dispose();
        _rejected.Dispose();
    }

    private int Apply(ReloadableConfiguration source, IOptionsMonitor<DatabaseOptions> monitor)
    {
        source.Set(("DatabaseOptions:Timeout", (++_next % 1000 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return monitor.CurrentValue.Timeout;
    }

    [Benchmark(Baseline = true)]
    public int FrameworkValidChange() => Apply(_frameworkSource, _frameworkMonitor);

    [Benchmark]
    public int SondorValidChange() => Apply(_sondorSource, _sondorMonitor);

    /// <summary>A change that is invalid is rejected and the last valid options are read back: no exception reaches the caller.</summary>
    [Benchmark]
    public int SondorRejectedChange()
    {
        _rejectedSource.Set(("DatabaseOptions:Timeout", "-1"));

        return _rejectedMonitor.CurrentValue.Timeout;
    }
}
