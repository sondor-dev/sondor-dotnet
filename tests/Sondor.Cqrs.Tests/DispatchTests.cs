using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class DispatchTests
{
    [Test]
    public async Task Send_Should_Return_The_Response_Of_The_Handler()
    {
        await using var provider = Provider.Build();

        var response = await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        Assert.That(response, Is.EqualTo("pong a"));
    }

    [Test]
    public async Task Send_Should_Handle_A_Plain_Request_With_Any_Response_Type()
    {
        await using var provider = Provider.Build();

        var response = await provider.GetRequiredService<ISender>().Send(new Plain(41));

        Assert.That(response, Is.EqualTo(42));
    }

    [Test]
    public async Task Send_Should_Return_Unit_For_A_Command_With_Nothing_To_Return()
    {
        await using var provider = Provider.Build();

        var response = await provider.GetRequiredService<ISender>().Send(new Rename("node-1"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo(Unit.Value));
            Assert.That(RenameHandler.Last, Is.EqualTo("node-1"));
        }
    }

    [Test]
    public async Task Send_Should_Let_The_Handler_Exception_Reach_The_Caller()
    {
        await using var provider = Provider.Build();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Explode()),
            Throws.InvalidOperationException.With.Message.EqualTo("boom"));
    }

    [Test]
    public async Task Send_Should_Fail_Clearly_When_There_Is_No_Handler()
    {
        var services = new ServiceCollection();
        services.AddCqrs(_ => { });
        await using var provider = services.BuildServiceProvider();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Ping("a")),
            Throws.InvalidOperationException.With.Message.Contains(typeof(Ping).FullName!));
    }

    [Test]
    public async Task Send_Should_Reject_A_Null_Request()
    {
        await using var provider = Provider.Build();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send<string>(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task Send_Should_Pass_The_Cancellation_Token_To_The_Handler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestHandler<TokenRequest, CancellationToken>, TokenHandler>();
        services.AddCqrs(_ => { });
        await using var provider = services.BuildServiceProvider();
        using var source = new CancellationTokenSource();

        var seen = await provider.GetRequiredService<ISender>().Send(new TokenRequest(), source.Token);

        Assert.That(seen, Is.EqualTo(source.Token));
    }

    [Test]
    public async Task Handlers_Should_Be_Transient_By_Default_And_Follow_The_Configured_Lifetime()
    {
        await using var transient = Provider.Build();
        await using var singleton = Provider.Build(cqrs => cqrs.HandlerLifetime = ServiceLifetime.Singleton);

        var transientFirst = transient.GetRequiredService<IRequestHandler<Ping, string>>();
        var transientSecond = transient.GetRequiredService<IRequestHandler<Ping, string>>();
        var singletonFirst = singleton.GetRequiredService<IRequestHandler<Ping, string>>();
        var singletonSecond = singleton.GetRequiredService<IRequestHandler<Ping, string>>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transientFirst, Is.Not.SameAs(transientSecond));
            Assert.That(singletonFirst, Is.SameAs(singletonSecond));
        }
    }

    [Test]
    public async Task Send_Should_Resolve_Scoped_Handlers_From_The_Scope_Of_The_Sender()
    {
        await using var provider = Provider.Build(services: services => services.AddScoped<ScopeMarker>());
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        var a = await first.ServiceProvider.GetRequiredService<ISender>().Send(new ScopeRequest());
        var b = await first.ServiceProvider.GetRequiredService<ISender>().Send(new ScopeRequest());
        var c = await second.ServiceProvider.GetRequiredService<ISender>().Send(new ScopeRequest());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }
    }

    internal sealed record TokenRequest : IRequest<CancellationToken>;

    internal sealed class TokenHandler : IRequestHandler<TokenRequest, CancellationToken>
    {
        public ValueTask<CancellationToken> Handle(TokenRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(cancellationToken);
    }

    internal sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    internal sealed record ScopeRequest : IRequest<Guid>;

    internal sealed class ScopeHandler(ScopeMarker marker) : IRequestHandler<ScopeRequest, Guid>
    {
        public ValueTask<Guid> Handle(ScopeRequest request, CancellationToken cancellationToken) => ValueTask.FromResult(marker.Id);
    }
}
