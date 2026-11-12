using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Tests;

internal sealed record Ping(string Text) : IQuery<string>;

internal sealed class PingHandler : IRequestHandler<Ping, string>
{
    public ValueTask<string> Handle(Ping request, CancellationToken cancellationToken) => ValueTask.FromResult($"pong {request.Text}");
}

internal sealed record Rename(string Name) : ICommand;

internal sealed class RenameHandler : IRequestHandler<Rename, Unit>
{
    public static string? Last { get; set; }

    public ValueTask<Unit> Handle(Rename request, CancellationToken cancellationToken)
    {
        Last = request.Name;
        return ValueTask.FromResult(Unit.Value);
    }
}

internal sealed record Plain(int Value) : IRequest<int>;

internal sealed class PlainHandler : IRequestHandler<Plain, int>
{
    public ValueTask<int> Handle(Plain request, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value + 1);
}

internal sealed record Explode : ICommand<int>;

internal sealed class ExplodeHandler : IRequestHandler<Explode, int>
{
    public ValueTask<int> Handle(Explode request, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
}

// A response that carries an outcome, the way a result type does.
internal sealed record Reply(bool Ok, string? Code = null);

internal sealed record Check(bool Pass) : IQuery<Reply>;

internal sealed class CheckHandler : IRequestHandler<Check, Reply>
{
    public ValueTask<Reply> Handle(Check request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Pass ? new Reply(true) : new Reply(false, "Check.Failed"));
}

internal sealed class ReplyOutcomeReader : IOutcomeReader<Reply>
{
    public RequestOutcome Read(Reply response) => response.Ok ? RequestOutcome.Success : RequestOutcome.Failure(response.Code);
}

internal sealed record Happened(string What) : INotification;

internal sealed class Recorder
{
    public List<string> Calls { get; } = [];
}

internal sealed class FirstHappenedHandler(Recorder recorder) : INotificationHandler<Happened>
{
    public ValueTask Handle(Happened notification, CancellationToken cancellationToken)
    {
        recorder.Calls.Add($"first:{notification.What}");
        return ValueTask.CompletedTask;
    }
}

internal sealed class SecondHappenedHandler(Recorder recorder) : INotificationHandler<Happened>
{
    public ValueTask Handle(Happened notification, CancellationToken cancellationToken)
    {
        recorder.Calls.Add($"second:{notification.What}");
        return ValueTask.CompletedTask;
    }
}


internal static class Provider
{
    public static ServiceProvider Build(Action<CqrsBuilder>? configure = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<Recorder>();
        services?.Invoke(collection);
        collection.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<PingHandler>();
            cqrs.ValidateHandlers = false;
            configure?.Invoke(cqrs);
        });

        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }
}
