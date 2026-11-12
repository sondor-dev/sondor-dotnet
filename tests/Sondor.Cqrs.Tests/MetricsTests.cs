using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class MetricsTests
{
    private readonly List<(string Name, long Count, Dictionary<string, object?> Tags)> _counts = [];
    private readonly List<(double Seconds, Dictionary<string, object?> Tags)> _durations = [];
    private MeterListener _listener = null!;

    [SetUp]
    public void Listen()
    {
        _counts.Clear();
        _durations.Clear();
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CqrsTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            lock (_counts)
            {
                _counts.Add((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
            }
        });
        _listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            lock (_durations)
            {
                _durations.Add((value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
            }
        });
        _listener.Start();
    }

    [TearDown]
    public void Stop() => _listener.Dispose();

    [Test]
    public async Task A_Request_Should_Be_Counted_And_Timed_With_Its_Name_Kind_And_Outcome()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseMetrics());

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        var count = _counts.Single(entry => Equals(entry.Tags["cqrs.request"], nameof(Ping)));
        var duration = _durations.Single(entry => Equals(entry.Tags["cqrs.request"], nameof(Ping)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(count.Name, Is.EqualTo("cqrs.requests"));
            Assert.That(count.Count, Is.EqualTo(1));
            Assert.That(count.Tags["cqrs.request.kind"], Is.EqualTo("query"));
            Assert.That(count.Tags["cqrs.outcome"], Is.EqualTo("success"));
            Assert.That(duration.Seconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(duration.Tags["cqrs.outcome"], Is.EqualTo("success"));
        }
    }

    [Test]
    public async Task A_Failure_That_Is_Returned_Should_Be_Counted_As_A_Failure_When_The_Response_Can_Be_Read()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseMetrics(),
            services => services.AddSingleton<IOutcomeReader<Reply>, ReplyOutcomeReader>());

        await provider.GetRequiredService<ISender>().Send(new Check(false));

        Assert.That(_counts.Single(entry => Equals(entry.Tags["cqrs.request"], nameof(Check))).Tags["cqrs.outcome"], Is.EqualTo("failure"));
    }

    [Test]
    public async Task An_Exception_Should_Be_Counted_As_An_Error_And_Still_Reach_The_Caller()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseMetrics());

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Explode()), Throws.InvalidOperationException);

        Assert.That(_counts.Single(entry => Equals(entry.Tags["cqrs.request"], nameof(Explode))).Tags["cqrs.outcome"], Is.EqualTo("error"));
    }

    [Test]
    public async Task A_Cancelled_Request_Should_Be_Counted_As_Cancelled()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseMetrics(),
            services => services.AddTransient<IRequestHandler<Cancelling, int>, CancellingHandler>());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Cancelling(), source.Token), Throws.InstanceOf<OperationCanceledException>());

        Assert.That(_counts.Single(entry => Equals(entry.Tags["cqrs.request"], nameof(Cancelling))).Tags["cqrs.outcome"], Is.EqualTo("cancelled"));
    }

    [Test]
    public async Task Nothing_Should_Be_Recorded_When_Metrics_Are_Not_Asked_For()
    {
        await using var provider = Provider.Build();

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        Assert.That(_counts.Where(entry => Equals(entry.Tags["cqrs.request"], nameof(Ping))), Is.Empty);
    }

    internal sealed record Cancelling : ICommand<int>;

    internal sealed class CancellingHandler : IRequestHandler<Cancelling, int>
    {
        public ValueTask<int> Handle(Cancelling request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }
}
