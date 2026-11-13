using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class LoggingTests
{
    [Test]
    public async Task A_Handled_Request_Should_Be_Logged_With_Its_Kind_And_Name()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log);

        await provider.GetRequiredService<ISender>().Send(new Ping("a"));

        Assert.That(log.Entries, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Information));
            Assert.That(log.Entries[0].Message, Does.StartWith("Handled query Ping in "));
            Assert.That(log.Categories, Is.EqualTo(new[] { CqrsTelemetry.SourceName }));
        }
    }

    [Test]
    public async Task The_Logger_Should_Be_Asked_For_Once_And_Not_For_Every_Request()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log);
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new Ping("a"));
        await sender.Send(new Ping("b"));
        await sender.Send(new Ping("c"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries, Has.Count.EqualTo(3));
            Assert.That(log.Categories, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task The_Request_Itself_Should_Never_Be_Logged()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log);

        await provider.GetRequiredService<ISender>().Send(new Ping("hunter2-secret"));

        Assert.That(log.Entries.Select(entry => entry.Message), Has.None.Contain("hunter2-secret"));
    }

    [Test]
    public async Task A_Failure_That_Is_Returned_Should_Be_Logged_With_Its_Code_When_The_Response_Can_Be_Read()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log, services => services.AddSingleton<IOutcomeReader<Reply>, ReplyOutcomeReader>());

        await provider.GetRequiredService<ISender>().Send(new Check(false));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Information));
            Assert.That(log.Entries[0].Message, Does.StartWith("Refused query Check with Check.Failed in "));
        }
    }

    [Test]
    public async Task A_Failure_Without_A_Code_Should_Still_Be_Logged()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log, services => services.AddSingleton<IOutcomeReader<Reply>, CodelessReader>());

        await provider.GetRequiredService<ISender>().Send(new Check(false));

        Assert.That(log.Entries[0].Message, Does.Contain("(no code)"));
    }

    [Test]
    public async Task A_Slow_Request_Should_Be_A_Warning()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log, threshold: TimeSpan.FromTicks(1), slow: true);

        await provider.GetRequiredService<ISender>().Send(new Delay());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(log.Entries[0].Message, Does.Contain("over the"));
        }
    }

    [Test]
    public async Task An_Exception_Should_Be_Logged_As_Its_Type_Only_And_Still_Reach_The_Caller()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log);

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Explode()), Throws.InvalidOperationException);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Error));
            Assert.That(log.Entries[0].Message, Does.StartWith("command Explode threw InvalidOperationException after "));
            Assert.That(log.Entries[0].Message, Does.Not.Contain("boom"));
        }
    }

    [Test]
    public async Task A_Cancelled_Request_Should_Be_Logged_Without_An_Error()
    {
        var log = new CapturingLoggerFactory();
        await using var provider = Build(log, services => services.AddTransient<IRequestHandler<Stop, int>, StopHandler>());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        Assert.That(async () => await provider.GetRequiredService<ISender>().Send(new Stop(), source.Token), Throws.InstanceOf<OperationCanceledException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Information));
            Assert.That(log.Entries[0].Message, Does.StartWith("Cancelled command Stop after "));
        }
    }

    private static ServiceProvider Build(CapturingLoggerFactory log,
        Action<IServiceCollection>? services = null,
        TimeSpan? threshold = null,
        bool slow = false) =>
        Provider.Build(cqrs => cqrs.UseLogging(threshold),
            collection =>
            {
                collection.AddSingleton<ILoggerFactory>(log);
                collection.AddTransient<IRequestHandler<Delay, int>, DelayHandler>();
                services?.Invoke(collection);
            });

    internal sealed class CodelessReader : IOutcomeReader<Reply>
    {
        public RequestOutcome Read(Reply response) => RequestOutcome.Failure(null);
    }

    internal sealed record Delay : IQuery<int>;

    internal sealed class DelayHandler : IRequestHandler<Delay, int>
    {
        public async ValueTask<int> Handle(Delay request, CancellationToken cancellationToken)
        {
            await Task.Delay(5, cancellationToken);
            return 1;
        }
    }

    internal sealed record Stop : ICommand<int>;

    internal sealed class StopHandler : IRequestHandler<Stop, int>
    {
        public ValueTask<int> Handle(Stop request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(0);
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public List<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            Categories.Add(categoryName);
            return new CapturingLogger(Entries);
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Add((logLevel, formatter(state, exception)));
    }
}
