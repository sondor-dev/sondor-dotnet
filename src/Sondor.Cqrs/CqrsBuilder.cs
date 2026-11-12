using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sondor.Cqrs.Behaviors;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs;

/// <summary>
/// Configures <c>Sondor.Cqrs</c> inside <see cref="ServiceCollectionExtensions.AddCqrs"/>: which assemblies hold the handlers,
/// which behaviours run, and in which order. Nothing is on unless it is asked for.
/// </summary>
public sealed class CqrsBuilder
{
    private readonly IServiceCollection _services;
    private readonly List<Assembly> _assemblies = [];
    private readonly List<Type> _behaviors = [];
    private Func<IServiceProvider, INotificationPublisher> _publisher = static _ => new SequentialNotificationPublisher();
    private bool _traceNotifications;

    internal CqrsBuilder(IServiceCollection services) => _services = services;

    /// <summary>The lifetime of the handlers that are found. Transient unless set; set it before <c>AddCqrs</c> ends.</summary>
    public ServiceLifetime HandlerLifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// Whether <c>AddCqrs</c> checks that every request type in the scanned assemblies has a handler, and fails with the names of
    /// those that have none. On unless turned off. Two handlers for one request always fail: that is never what was meant.
    /// </summary>
    public bool ValidateHandlers { get; set; } = true;

    /// <summary>
    /// Registers the request and notification handlers found in the assemblies. Handlers that are generic classes are not
    /// found: register those yourself.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The builder.</returns>
    public CqrsBuilder AddHandlersFromAssembly(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        _assemblies.AddRange(assemblies);

        return this;
    }

    /// <summary>Registers the handlers found in the assembly that holds <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">A type in the assembly.</typeparam>
    /// <returns>The builder.</returns>
    public CqrsBuilder AddHandlersFromAssemblyContaining<T>() => AddHandlersFromAssembly(typeof(T).Assembly);

    /// <summary>
    /// Adds a behaviour to the pipeline, after those added before it: the first one added is the outermost. Give an open
    /// generic type (<c>typeof(MyBehavior&lt;,&gt;)</c>) to run for every request it can, or a closed type for one request.
    /// </summary>
    /// <param name="behaviorType">The behaviour class, implementing <see cref="IPipelineBehavior{TRequest, TResponse}"/>.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">The type is not a behaviour.</exception>
    public CqrsBuilder AddBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsClass || behaviorType.IsAbstract || !ImplementsBehavior(behaviorType))
        {
            throw new ArgumentException(
                $"{behaviorType.FullName} is not a pipeline behaviour: it must be a class that implements IPipelineBehavior<TRequest, TResponse>.",
                nameof(behaviorType));
        }

        _behaviors.Add(behaviorType);

        return this;
    }

    /// <summary>Uses <typeparamref name="TPublisher"/> to run the handlers of a notification. The default runs them one after another.</summary>
    /// <typeparam name="TPublisher">The publisher. Built by the container, once.</typeparam>
    /// <returns>The builder.</returns>
    public CqrsBuilder UseNotificationPublisher<TPublisher>()
        where TPublisher : class, INotificationPublisher
    {
        _publisher = static services => ActivatorUtilities.CreateInstance<TPublisher>(services);

        return this;
    }

    /// <summary>Uses this publisher to run the handlers of a notification. The default runs them one after another.</summary>
    /// <param name="publisher">The publisher.</param>
    /// <returns>The builder.</returns>
    public CqrsBuilder UseNotificationPublisher(INotificationPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        _publisher = _ => publisher;

        return this;
    }

    /// <summary>
    /// Adds the tracing behaviour: a span for each request and for each notification and its handlers, source
    /// <see cref="CqrsTelemetry.SourceName"/>. It does nothing while no listener is attached.
    /// </summary>
    /// <returns>The builder.</returns>
    public CqrsBuilder UseTracing()
    {
        _traceNotifications = true;
        _behaviors.Add(typeof(TracingBehavior<,>));

        return this;
    }

    /// <summary>
    /// Adds the metrics behaviour: a request counter and a duration histogram, meter <see cref="CqrsTelemetry.MeterName"/>. It
    /// does nothing while nothing is listening.
    /// </summary>
    /// <returns>The builder.</returns>
    public CqrsBuilder UseMetrics()
    {
        _behaviors.Add(typeof(MetricsBehavior<,>));

        return this;
    }

    /// <summary>Adds the logging behaviour: name, duration and outcome of each request, never the request itself.</summary>
    /// <param name="slowRequestThreshold">A request that takes longer is logged as a warning. One second unless set.</param>
    /// <returns>The builder.</returns>
    public CqrsBuilder UseLogging(TimeSpan? slowRequestThreshold = null)
    {
        var threshold = slowRequestThreshold ?? TimeSpan.FromSeconds(1);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(threshold, TimeSpan.Zero, nameof(slowRequestThreshold));

        _services.Replace(ServiceDescriptor.Singleton(new LoggingSettings(threshold)));
        _behaviors.Add(typeof(LoggingBehavior<,>));

        return this;
    }

    internal void Apply()
    {
        HandlerScanner.Register(_services, _assemblies, HandlerLifetime, ValidateHandlers);

        foreach (var behavior in _behaviors)
        {
            if (behavior.IsGenericTypeDefinition)
            {
                _services.Add(ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), behavior));
                continue;
            }

            foreach (var contract in behavior.GetInterfaces().Where(IsBehaviorContract))
            {
                _services.Add(ServiceDescriptor.Transient(contract, behavior));
            }
        }

        var publisher = _publisher;
        var trace = _traceNotifications;

        _services.RemoveAll<INotificationPublisher>();
        _services.AddSingleton<INotificationPublisher>(services =>
            trace ? new TracingNotificationPublisher(publisher(services)) : publisher(services));

        _services.TryAddTransient<ISender, Mediator>();
        _services.TryAddTransient<IPublisher, Mediator>();
    }

    private static bool ImplementsBehavior(Type type) => type.GetInterfaces().Any(IsBehaviorContract);

    private static bool IsBehaviorContract(Type contract) =>
        contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>);
}
