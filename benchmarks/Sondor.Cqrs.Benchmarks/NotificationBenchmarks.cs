using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Benchmarks;

/// <summary>What it costs to publish a notification to three handlers that do nothing, one after another.</summary>
[MemoryDiagnoser]
public class NotificationBenchmarks
{
    private readonly Happened _notification = new(1);
    private ServiceProvider _provider = null!;
    private IPublisher _publisher = null!;

    [GlobalSetup]
    public void Setup()
    {
        _provider = Containers.Build(notifications: true);
        _publisher = _provider.GetRequiredService<IPublisher>();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark]
    public ValueTask PublishToThreeHandlers() => _publisher.Publish(_notification);
}
