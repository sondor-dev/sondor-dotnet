using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sondor.Cqrs.Benchmarks;

internal sealed record Ping(int Value) : IQuery<int>;

internal sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> Handle(Ping request, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value + 1);
}

internal sealed record Happened(int Value) : INotification;

internal sealed class FirstHandler : INotificationHandler<Happened>
{
    public ValueTask Handle(Happened notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class SecondHandler : INotificationHandler<Happened>
{
    public ValueTask Handle(Happened notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

internal sealed class ThirdHandler : INotificationHandler<Happened>
{
    public ValueTask Handle(Happened notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

// Does nothing but call on, so what is measured is the cost of a behaviour in the pipeline, not of what it does.
internal sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken) =>
        continuation();
}

internal static class Containers
{
    /// <summary>A container with the handlers and the behaviours the scenario asks for. Handlers are registered by hand, so the scan is not measured.</summary>
    public static ServiceProvider Build(Action<CqrsBuilder>? configure = null, bool notifications = false)
    {
        var services = new ServiceCollection();

        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddTransient<IRequestHandler<Ping, int>, PingHandler>();

        if (notifications)
        {
            services.AddTransient<INotificationHandler<Happened>, FirstHandler>();
            services.AddTransient<INotificationHandler<Happened>, SecondHandler>();
            services.AddTransient<INotificationHandler<Happened>, ThirdHandler>();
        }

        services.AddCqrs(cqrs =>
        {
            cqrs.ValidateHandlers = false;
            configure?.Invoke(cqrs);
        });

        return services.BuildServiceProvider();
    }

    /// <summary>Listeners that record, so the cost of tracing and metrics when something is listening can be measured.</summary>
    public sealed class Listeners : IDisposable
    {
        private readonly ActivityListener _activities = new()
        {
            ShouldListenTo = source => source.Name == CqrsTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        private readonly MeterListener _meters = new();

        public Listeners()
        {
            ActivitySource.AddActivityListener(_activities);
            _meters.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CqrsTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meters.SetMeasurementEventCallback<long>(static (_, _, _, _) => { });
            _meters.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
            _meters.Start();
        }

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }
    }
}

