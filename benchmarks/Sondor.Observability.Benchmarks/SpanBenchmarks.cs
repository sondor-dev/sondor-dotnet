using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Sondor.Observability.Benchmarks;

/// <summary>
/// What a traced operation costs: starting and ending one span, with the OpenTelemetry SDK set up by hand against the same
/// thing set up by Sondor.Observability. Nothing is exported, so what is measured is the sampler and the listener, which every
/// span pays whether or not it is kept.
/// </summary>
[MemoryDiagnoser]
public class SpanBenchmarks : IDisposable
{
    private ActivitySource _plainSource = null!;
    private ActivitySource _keepSource = null!;
    private ActivitySource _dropSource = null!;
    private ActivitySource _offSource = null!;
    private TracerProvider _plain = null!;
    private IHost _keep = null!;
    private IHost _drop = null!;
    private IHost _off = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plainSource = new ActivitySource("bench.plain");
        _keepSource = new ActivitySource("bench.keep");
        _dropSource = new ActivitySource("bench.drop");
        _offSource = new ActivitySource("bench.off");

        _plain = Sdk.CreateTracerProviderBuilder()
            .AddSource("bench.plain")
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(1)))
            .Build();

        _keep = Hosts.Start(new ReloadableConfiguration { }.Prepared(("Observability:Traces:SamplingRatio", "1")), "bench.keep");
        _drop = Hosts.Start(new ReloadableConfiguration { }.Prepared(("Observability:Traces:SamplingRatio", "0")), "bench.drop");
        _off = Hosts.Start(new ReloadableConfiguration { }.Prepared(("Observability:Traces:Mode", "Off")), "bench.off");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _plain.Dispose();
        _keep.Dispose();
        _drop.Dispose();
        _off.Dispose();
        _plainSource.Dispose();
        _keepSource.Dispose();
        _dropSource.Dispose();
        _offSource.Dispose();
    }

    [Benchmark(Baseline = true, Description = "OpenTelemetry by hand, ratio 1")]
    public bool Plain() => Spans.Run(_plainSource);

    [Benchmark(Description = "Sondor.Observability, ratio 1")]
    public bool Keep() => Spans.Run(_keepSource);

    [Benchmark(Description = "Sondor.Observability, ratio 0")]
    public bool Drop() => Spans.Run(_dropSource);

    [Benchmark(Description = "Sondor.Observability, traces off")]
    public bool Off() => Spans.Run(_offSource);
}

internal static class ConfigurationExtensions
{
    public static ReloadableConfiguration Prepared(this ReloadableConfiguration configuration, params (string Key, string? Value)[] values)
    {
        configuration.Set(values);

        return configuration;
    }
}
