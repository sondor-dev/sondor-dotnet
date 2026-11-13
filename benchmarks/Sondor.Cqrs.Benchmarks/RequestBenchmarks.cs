using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Benchmarks;

/// <summary>
/// What it costs to send a request: against resolving the handler from the container and calling it, then with behaviours in the
/// pipeline. Each scenario sends through one long-lived scope, the way a request does. The handlers do no work, so what is measured
/// is the library.
/// </summary>
[MemoryDiagnoser]
public class RequestBenchmarks
{
    private readonly Ping _request = new(41);
    private ServiceProvider _plain = null!;
    private ServiceProvider _threeBehaviors = null!;
    private ServiceProvider _builtIns = null!;
    private ISender _plainSender = null!;
    private ISender _threeSender = null!;
    private ISender _builtInsSender = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plain = Containers.Build();
        _threeBehaviors = Containers.Build(cqrs => cqrs
            .AddBehavior(typeof(PassThroughBehavior<,>))
            .AddBehavior(typeof(PassThroughBehavior<,>))
            .AddBehavior(typeof(PassThroughBehavior<,>)));
        _builtIns = Containers.Build(cqrs => cqrs.UseTracing().UseMetrics().UseLogging());

        _plainSender = _plain.GetRequiredService<ISender>();
        _threeSender = _threeBehaviors.GetRequiredService<ISender>();
        _builtInsSender = _builtIns.GetRequiredService<ISender>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _plain.Dispose();
        _threeBehaviors.Dispose();
        _builtIns.Dispose();
    }

    /// <summary>The floor: the handler resolved from the container and called, with no dispatcher.</summary>
    [Benchmark(Baseline = true)]
    public ValueTask<int> HandlerFromContainer() => _plain.GetRequiredService<IRequestHandler<Ping, int>>().Handle(_request, CancellationToken.None);

    /// <summary>Through the dispatcher, no behaviours.</summary>
    [Benchmark]
    public ValueTask<int> Send() => _plainSender.Send(_request);

    /// <summary>Through the dispatcher and three behaviours that only call on.</summary>
    [Benchmark]
    public ValueTask<int> SendWithThreeBehaviors() => _threeSender.Send(_request);

    /// <summary>Tracing, metrics and logging added, with nothing listening and the logger off: what an unobserved service pays.</summary>
    [Benchmark]
    public ValueTask<int> SendWithBuiltInsNothingListening() => _builtInsSender.Send(_request);
}
