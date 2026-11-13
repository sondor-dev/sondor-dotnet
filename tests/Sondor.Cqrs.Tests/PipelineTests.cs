using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class PipelineTests
{
    [Test]
    public void A_Default_Continuation_Should_Say_It_Is_Not_Part_Of_A_Pipeline()
    {
        var continuation = default(RequestContinuation<int>);

        Assert.That(() => continuation.Invoke(), Throws.InvalidOperationException.With.Message.Contains("not part of a pipeline"));
    }

    [Test]
    public async Task A_Retrying_Behavior_Should_Run_Everything_Inside_It_Again()
    {
        await using var provider = Provider.Build(cqrs => cqrs
            .AddBehavior(typeof(RetryOnceBehavior))
            .AddBehavior(typeof(CountingBehavior)));

        var response = await provider.GetRequiredService<ISender>().Send(new Plain(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo(2));
            Assert.That(provider.GetRequiredService<Recorder>().Calls, Is.EqualTo(new List<string> { "retry", "inner", "retry", "inner" }));
        }
    }

    [Test]
    public async Task A_Continuation_Should_Survive_An_Await_Before_It_Is_Invoked()
    {
        await using var provider = Provider.Build(cqrs => cqrs.AddBehavior(typeof(YieldFirstBehavior)));

        var response = await provider.GetRequiredService<ISender>().Send(new Plain(10));

        Assert.That(response, Is.EqualTo(11));
    }

    [Test]
    public async Task Requests_Sent_At_The_Same_Time_Should_Not_Share_A_Pipeline()
    {
        await using var provider = Provider.Build(cqrs => cqrs
            .AddBehavior(typeof(YieldFirstBehavior))
            .AddBehavior(typeof(CountingBehavior)));
        var sender = provider.GetRequiredService<ISender>();

        var responses = await Task.WhenAll(Enumerable.Range(0, 200).Select(value => sender.Send(new Plain(value)).AsTask()));

        Assert.That(responses, Is.EqualTo(Enumerable.Range(1, 200).ToArray()));
    }

    internal sealed class RetryOnceBehavior(Recorder recorder) : IPipelineBehavior<Plain, int>
    {
        public async ValueTask<int> Handle(Plain request, RequestContinuation<int> continuation, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("retry");
            await continuation.Invoke();
            recorder.Calls.Add("retry");

            return await continuation.Invoke();
        }
    }

    internal sealed class CountingBehavior(Recorder recorder) : IPipelineBehavior<Plain, int>
    {
        public ValueTask<int> Handle(Plain request, RequestContinuation<int> continuation, CancellationToken cancellationToken)
        {
            lock (recorder)
            {
                recorder.Calls.Add("inner");
            }

            return continuation.Invoke();
        }
    }

    internal sealed class YieldFirstBehavior : IPipelineBehavior<Plain, int>
    {
        public async ValueTask<int> Handle(Plain request, RequestContinuation<int> continuation, CancellationToken cancellationToken)
        {
            await Task.Yield();

            return await continuation.Invoke();
        }
    }
}
