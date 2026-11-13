using Microsoft.Extensions.DependencyInjection;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class RegistrationTests
{
    [Test]
    public void AddCqrs_Should_Reject_Null_Arguments()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => ServiceCollectionExtensions.AddCqrs(null!, _ => { }), Throws.ArgumentNullException);
            Assert.That(() => new ServiceCollection().AddCqrs(null!), Throws.ArgumentNullException);
        }
    }

    [Test]
    public void AddCqrs_Should_Fail_With_The_Names_When_A_Request_Has_Two_Handlers()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HandlerScanner.Register(services, [typeof(Invalid<int>.FirstDuplicateHandler), typeof(Invalid<int>.SecondDuplicateHandler)],
                ServiceLifetime.Transient, true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.Contain("DuplicateRequest"));
            Assert.That(exception.Message, Does.Contain("FirstDuplicateHandler"));
            Assert.That(exception.Message, Does.Contain("SecondDuplicateHandler"));
        }
    }

    [Test]
    public void AddCqrs_Should_Fail_With_The_Names_When_A_Request_Has_No_Handler()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            HandlerScanner.Register(services, [typeof(Invalid<int>.OrphanRequest)], ServiceLifetime.Transient, true));

        Assert.That(exception.Message, Does.Contain("OrphanRequest"));
    }

    [Test]
    public void AddCqrs_Should_Find_A_Handler_For_Every_Request_Of_The_Scanned_Assembly()
    {
        // The scan skips the open generic fixture classes below, so only well-formed requests are in the assembly.
        Assert.That(() => new ServiceCollection().AddCqrs(cqrs => cqrs.AddHandlersFromAssemblyContaining<RegistrationTests>()), Throws.Nothing);
    }

    [Test]
    public void AddCqrs_Should_Not_Check_For_Missing_Handlers_When_Asked_Not_To()
    {
        var services = new ServiceCollection();

        Assert.That(() => services.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<PingHandler>();
            cqrs.ValidateHandlers = false;
        }), Throws.Nothing);
    }

    [Test]
    public void AddCqrs_Should_Accept_A_Handler_That_Was_Registered_By_Hand()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<Invalid<int>.OrphanRequest, int>, Invalid<int>.OrphanHandler>();

        Assert.That(() => HandlerScanner.Register(services, [typeof(Invalid<int>.OrphanRequest)], ServiceLifetime.Transient, true), Throws.Nothing);
    }

    [Test]
    public async Task A_Handler_Registered_After_The_Scan_Should_Replace_The_Scanned_One()
    {
        var services = new ServiceCollection();
        services.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<PingHandler>();
            cqrs.ValidateHandlers = false;
        });
        services.AddTransient<IRequestHandler<Ping, string>, Invalid<int>.FakePingHandler>();
        await using var provider = services.BuildServiceProvider();

        Assert.That(await provider.GetRequiredService<ISender>().Send(new Ping("a")), Is.EqualTo("fake"));
    }

    [Test]
    public void AddBehavior_Should_Reject_A_Type_That_Is_Not_A_Behavior()
    {
        var services = new ServiceCollection();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => services.AddCqrs(cqrs => cqrs.AddBehavior(typeof(string))), Throws.ArgumentException);
            Assert.That(() => services.AddCqrs(cqrs => cqrs.AddBehavior(typeof(AbstractBehavior))), Throws.ArgumentException);
            Assert.That(() => services.AddCqrs(cqrs => cqrs.AddBehavior(null!)), Throws.ArgumentNullException);
        }
    }

    [Test]
    public async Task Behaviors_Should_Run_In_The_Order_They_Were_Added_Outermost_First()
    {
        await using var provider = Provider.Build(cqrs => cqrs
            .AddBehavior(typeof(FirstBehavior<,>))
            .AddBehavior(typeof(SecondBehavior<,>)));

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        Assert.That(provider.GetRequiredService<Recorder>().Calls, Is.EqualTo(new List<string> { "First>", "Secon>", "<", "<" }));
    }

    [Test]
    public async Task A_Closed_Behavior_Should_Run_For_Its_Request_Only()
    {
        await using var provider = Provider.Build(cqrs => cqrs.AddBehavior(typeof(PingOnlyBehavior)));

        await provider.GetRequiredService<ISender>().Send(new Plain(1));
        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        Assert.That(provider.GetRequiredService<Recorder>().Calls, Is.EqualTo(new List<string> { "ping", "<" }));
    }

    [Test]
    public async Task A_Behavior_May_Stop_The_Request_Without_Calling_The_Handler()
    {
        await using var provider = Provider.Build(cqrs => cqrs.AddBehavior(typeof(RefusingBehavior)));

        Assert.That(await provider.GetRequiredService<ISender>().Send(new Ping("a")), Is.EqualTo("refused"));
    }

    [Test]
    public async Task A_Behavior_May_Call_The_Rest_Of_The_Pipeline_More_Than_Once()
    {
        await using var provider = Provider.Build(cqrs => cqrs.AddBehavior(typeof(TwiceBehavior)));

        Assert.That(await provider.GetRequiredService<ISender>().Send(new Plain(1)), Is.EqualTo(4));
    }

    [Test]
    public async Task UseLogging_Should_Reject_A_Threshold_That_Is_Not_Positive()
    {
        await using var provider = Provider.Build();

        Assert.That(() => new ServiceCollection().AddCqrs(cqrs => cqrs.UseLogging(TimeSpan.Zero)), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    // Handlers that must stay out of the assembly scan the other tests use: malformed ones (two handlers for one request, a request
    // with none), a second handler of Ping, and one that takes a constructor argument. They sit in an open generic class, which the
    // scan skips, and are used closed (Invalid<int>) by the test that needs them.
    internal static class Invalid<T>
    {
        internal sealed class FakePingHandler : IRequestHandler<Ping, string>
        {
            public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken) => ValueTask.FromResult("fake");
        }

        internal sealed class Throwing(string message) : INotificationHandler<Happened>
        {
            public ValueTask Handle(Happened notification, CancellationToken cancellationToken) => throw new InvalidOperationException(message);
        }

        internal sealed record DuplicateRequest : IRequest<int>;

        internal sealed class FirstDuplicateHandler : IRequestHandler<DuplicateRequest, int>
        {
            public ValueTask<int> Handle(DuplicateRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(1);
        }

        internal sealed class SecondDuplicateHandler : IRequestHandler<DuplicateRequest, int>
        {
            public ValueTask<int> Handle(DuplicateRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(2);
        }

        internal sealed record OrphanRequest : IRequest<int>;

        internal sealed class OrphanHandler : IRequestHandler<OrphanRequest, int>
        {
            public ValueTask<int> Handle(OrphanRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(0);
        }
    }

    internal abstract class AbstractBehavior : IPipelineBehavior<Ping, string>
    {
        public abstract ValueTask<string> Handle(Ping request, RequestContinuation<string> continuation, CancellationToken cancellationToken);
    }

    internal sealed class FirstBehavior<TRequest, TResponse>(Recorder recorder) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("First>");
            var response = await continuation.Invoke();
            recorder.Calls.Add("<");
            return response;
        }
    }

    internal sealed class SecondBehavior<TRequest, TResponse>(Recorder recorder) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("Secon>");
            var response = await continuation.Invoke();
            recorder.Calls.Add("<");
            return response;
        }
    }

    internal sealed class PingOnlyBehavior(Recorder recorder) : IPipelineBehavior<Ping, string>
    {
        public async ValueTask<string> Handle(Ping request, RequestContinuation<string> continuation, CancellationToken cancellationToken)
        {
            recorder.Calls.Add("ping");
            var response = await continuation.Invoke();
            recorder.Calls.Add("<");
            return response;
        }
    }

    internal sealed class RefusingBehavior : IPipelineBehavior<Ping, string>
    {
        public ValueTask<string> Handle(Ping request, RequestContinuation<string> continuation, CancellationToken cancellationToken) =>
            ValueTask.FromResult("refused");
    }

    internal sealed class TwiceBehavior : IPipelineBehavior<Plain, int>
    {
        public async ValueTask<int> Handle(Plain request, RequestContinuation<int> continuation, CancellationToken cancellationToken) =>
            await continuation.Invoke() + await continuation.Invoke() + 0;
    }
}
