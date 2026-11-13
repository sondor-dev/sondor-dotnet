using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Benchmarks;

/// <summary>
/// What tracing and metrics cost while something is listening: a listener records every span and every measurement. In its own
/// class so the listeners, which are process-wide, are never attached while the scenarios that need nothing listening run.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "BenchmarkDotNet owns the lifetime: it calls GlobalSetup and GlobalCleanup, and benchmark classes cannot be sealed to follow the dispose pattern.")]
[MemoryDiagnoser]
public class ObservedRequestBenchmarks
{
    private readonly Ping _request = new(41);
    private Containers.Listeners _listeners = null!;
    private ServiceProvider _provider = null!;
    private ISender _sender = null!;

    [GlobalSetup]
    public void Setup()
    {
        _listeners = new Containers.Listeners();
        _provider = Containers.Build(cqrs => cqrs.UseTracing().UseMetrics().UseLogging());
        _sender = _provider.GetRequiredService<ISender>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _provider.Dispose();
        _listeners.Dispose();
    }

    [Benchmark]
    public ValueTask<int> SendWithBuiltInsListening() => _sender.Send(_request);
}
