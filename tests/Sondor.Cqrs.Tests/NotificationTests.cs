using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class NotificationTests
{
    [Test]
    public async Task Publish_Should_Run_Every_Handler_In_Registration_Order()
    {
        await using var provider = Provider.Build();

        await provider.GetRequiredService<IPublisher>().Publish(new Happened("x"));

        Assert.That(provider.GetRequiredService<Recorder>().Calls, Is.EqualTo(new List<string> { "first:x", "second:x" }));
    }

    [Test]
    public async Task Publish_Should_Do_Nothing_When_There_Is_No_Handler()
    {
        await using var provider = Provider.Build();

        Assert.That(async () => await provider.GetRequiredService<IPublisher>().Publish(new Unhandled()), Throws.Nothing);
    }

    [Test]
    public async Task Publish_Should_Find_The_Handlers_Of_The_Runtime_Type()
    {
        await using var provider = Provider.Build();
        INotification notification = new Happened("via base");

        await provider.GetRequiredService<IPublisher>().Publish(notification);

        Assert.That(provider.GetRequiredService<Recorder>().Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Publish_Should_Reject_A_Null_Notification()
    {
        await using var provider = Provider.Build();

        Assert.That(async () => await provider.GetRequiredService<IPublisher>().Publish(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task The_Sequential_Publisher_Should_Stop_At_The_First_Handler_That_Throws()
    {
        var recorder = new Recorder();
        var handlers = new INotificationHandler<Happened>[] { new RegistrationTests.Invalid<int>.Throwing("one"), new FirstHappenedHandler(recorder) };

        Assert.That(async () => await new SequentialNotificationPublisher().Publish(handlers, new Happened("x"), default),
            Throws.InvalidOperationException.With.Message.EqualTo("one"));
        await Task.CompletedTask;

        Assert.That(recorder.Calls, Is.Empty);
    }

    [Test]
    public void The_Parallel_Publisher_Should_Run_Every_Handler_Even_When_One_Throws()
    {
        var recorder = new Recorder();
        var handlers = new INotificationHandler<Happened>[] { new RegistrationTests.Invalid<int>.Throwing("one"), new FirstHappenedHandler(recorder), new SecondHappenedHandler(recorder) };

        Assert.That(async () => await new ParallelNotificationPublisher().Publish(handlers, new Happened("x"), default),
            Throws.InvalidOperationException.With.Message.EqualTo("one"));

        Assert.That(recorder.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task The_Parallel_Publisher_Should_Throw_Every_Failure_Together()
    {
        var handlers = new INotificationHandler<Happened>[] { new RegistrationTests.Invalid<int>.Throwing("one"), new RegistrationTests.Invalid<int>.Throwing("two") };

        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
            await new ParallelNotificationPublisher().Publish(handlers, new Happened("x"), default));

        Assert.That(exception!.InnerExceptions.Select(inner => inner.Message), Is.EquivalentTo(new List<string> { "one", "two" }));
    }

    [Test]
    public async Task The_Notification_Publisher_Can_Be_Replaced()
    {
        await using var provider = Provider.Build(cqrs => cqrs.UseNotificationPublisher<ParallelNotificationPublisher>());

        Assert.That(provider.GetRequiredService<INotificationPublisher>(), Is.TypeOf<ParallelNotificationPublisher>());
    }

    [Test]
    public async Task The_Notification_Publisher_Can_Be_Given_As_An_Instance()
    {
        var publisher = new ParallelNotificationPublisher();
        await using var provider = Provider.Build(cqrs => cqrs.UseNotificationPublisher(publisher));

        Assert.That(provider.GetRequiredService<INotificationPublisher>(), Is.SameAs(publisher));
    }

    [Test]
    public void Publishers_Should_Reject_A_Null_Handler_List()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(async () => await new SequentialNotificationPublisher().Publish<Happened>(null!, new Happened("x"), default), Throws.ArgumentNullException);
            Assert.That(async () => await new ParallelNotificationPublisher().Publish<Happened>(null!, new Happened("x"), default), Throws.ArgumentNullException);
        }
    }

    internal sealed record Unhandled : INotification;

}
