using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Hosting;

namespace Sondor.Observability.Benchmarks;

/// <summary>
/// One configuration change, and one span after it. Each case first goes through <see cref="PriorReloads"/> changes, so a
/// cost that grows with the number of changes already seen (a listener or a sampler left behind by every change: the kind of
/// defect the Sondor.Cqrs tracing benchmarks found) shows up as a difference between the two parameter values.
/// </summary>
[MemoryDiagnoser]
public class ReloadBenchmarks : IDisposable
{
    private ReloadableConfiguration _configuration = null!;
    private ActivitySource _source = null!;
    private IHost _host = null!;
    private bool _flip;

    [Params(0, 10_000)]
    public int PriorReloads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _configuration = new ReloadableConfiguration().Prepared(("Observability:Traces:SamplingRatio", "1"));
        _source = new ActivitySource("bench.reload");
        _host = Hosts.Start(_configuration, "bench.reload");

        for (var reload = 0; reload < PriorReloads; reload++)
        {
            Change();
        }
    }

    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _host.Dispose();
        _source.Dispose();
    }

    [Benchmark(Description = "One sampling ratio change")]
    public void Reload() => Change();

    [Benchmark(Description = "One span after the changes")]
    public bool SpanAfterReloads() => Spans.Run(_source);

    private void Change()
    {
        _flip = !_flip;
        _configuration.Set(("Observability:Traces:SamplingRatio", _flip ? "1" : "0.5"));
    }
}
