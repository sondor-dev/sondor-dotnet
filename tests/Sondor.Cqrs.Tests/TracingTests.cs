using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class TracingTests
{
    private readonly List<Activity> _activities = [];
    private ActivityListener _listener = null!;

    [SetUp]
    public void Listen()
    {
        _activities.Clear();
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CqrsTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_activities)
                {
                    _activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [TearDown]
    public void Stop() => _listener.Dispose();

    [Test]
    public async Task A_Query_Should_Be_A_Span_Tagged_With_Its_Kind_Its_Handler_And_A_Success()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing());

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        var span = Single(nameof(Ping));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.GetTagItem("cqrs.request.kind"), Is.EqualTo("query"));
            Assert.That(span.GetTagItem("cqrs.request"), Is.EqualTo(typeof(Ping).FullName));
            Assert.That(span.GetTagItem("cqrs.handler"), Is.EqualTo(typeof(PingHandler).FullName));
            Assert.That(span.GetTagItem("cqrs.outcome"), Is.EqualTo("success"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
        }
    }

    [Test]
    public async Task Commands_And_Plain_Requests_Should_Be_Told_Apart()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing());

        await provider.GetRequiredService<ISender>().Send(new Rename("a"));
        await provider.GetRequiredService<ISender>().Send(new Plain(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Single(nameof(Rename)).GetTagItem("cqrs.request.kind"), Is.EqualTo("command"));
            Assert.That(Single(nameof(Plain)).GetTagItem("cqrs.request.kind"), Is.EqualTo("request"));
        }
    }

    [Test]
    public async Task A_Failure_That_Is_Returned_Should_Be_An_Error_With_Its_Code_When_The_Response_Can_Be_Read()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing(),
            services => services.AddSingleton<IOutcomeReader<Reply>, ReplyOutcomeReader>());

        await provider.GetRequiredService<ISender>().Send(new Check(false));

        var span = Single(nameof(Check));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.GetTagItem("cqrs.outcome"), Is.EqualTo("failure"));
            Assert.That(span.GetTagItem("cqrs.error.code"), Is.EqualTo("Check.Failed"));
            Assert.That(span.GetTagItem("error.type"), Is.EqualTo("Check.Failed"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.StatusDescription, Is.EqualTo("Check.Failed"));
        }
    }

    [Test]
    public async Task A_Response_Nobody_Can_Read_Should_Count_As_A_Success()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing());

        await provider.GetRequiredService<ISender>().Send(new Check(false));

        Assert.That(Single(nameof(Check)).GetTagItem("cqrs.outcome"), Is.EqualTo("success"));
    }

    [Test]
    public async Task An_Exception_Should_Be_Recorded_And_Still_Reach_The_Caller()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing());

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Explode()), Throws.InvalidOperationException);

        var span = Single(nameof(Explode));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.GetTagItem("cqrs.outcome"), Is.EqualTo("error"));
            Assert.That(span.GetTagItem("error.type"), Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(span.Events.Select(@event => @event.Name), Does.Contain("exception"));
        }
    }

    [Test]
    public async Task A_Cancelled_Request_Should_Not_Be_An_Error()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing(),
            services => services.AddTransient<IRequestHandler<Slow, int>, SlowHandler>());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Slow(), source.Token), Throws.InstanceOf<OperationCanceledException>());

        var span = Single(nameof(Slow));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.GetTagItem("cqrs.outcome"), Is.EqualTo("cancelled"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Unset));
        }
    }

    [Test]
    public async Task A_Notification_Should_Be_A_Span_With_A_Child_Span_For_Each_Handler()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing());

        await provider.GetRequiredService<IPublisher>().Publish(new Happened("x"));

        var notification = Single(nameof(Happened));
        var first = Single(nameof(FirstHappenedHandler));
        var second = Single(nameof(SecondHappenedHandler));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notification.GetTagItem("cqrs.request.kind"), Is.EqualTo("notification"));
            Assert.That(first.ParentId, Is.EqualTo(notification.Id));
            Assert.That(second.ParentId, Is.EqualTo(notification.Id));
        }
    }

    [Test]
    public async Task A_Notification_Handler_That_Throws_Should_Mark_Its_Span_And_The_Notification_Span()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseTracing(),
            services => services.AddTransient<INotificationHandler<Failing>, FailingHandler>());

        Assert.That(async () => await provider.GetRequiredService<IPublisher>().Publish(new Failing()), Throws.InvalidOperationException);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Single(nameof(Failing)).Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(Single(nameof(FailingHandler)).Status, Is.EqualTo(ActivityStatusCode.Error));
        }
    }

    [Test]
    public async Task Nothing_Should_Be_Recorded_When_Tracing_Is_Not_Asked_For()
    {
        await using var provider = Provider.Build();

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));
        await provider.GetRequiredService<IPublisher>().Publish(new Happened("x"));

        Assert.That(_activities, Is.Empty);
    }

    private Activity Single(string name)
    {
        lock (_activities)
        {
            return _activities.Single(activity => activity.OperationName == name);
        }
    }

    internal sealed record Slow : ICommand<int>;

    internal sealed class SlowHandler : IRequestHandler<Slow, int>
    {
        public ValueTask<int> Handle(Slow request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }

    internal sealed record Failing : INotification;

    internal sealed class FailingHandler : INotificationHandler<Failing>
    {
        public ValueTask Handle(Failing notification, CancellationToken cancellationToken) => throw new InvalidOperationException("nope");
    }
}
