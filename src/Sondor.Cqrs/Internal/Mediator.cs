using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs.Internal;

/// <summary>
/// The dispatcher. One instance for each scope (it is given the scope's provider, so scoped handlers and behaviours resolve from
/// it). The per-type work, finding the closed generic types, is done once and kept.
/// </summary>
internal sealed class Mediator(IServiceProvider services, INotificationPublisher notificationPublisher) :
    ISender,
    IPublisher
{
    public ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapper = RequestWrappers<TResponse>.For(request.GetType());

        return wrapper.Handle(request, services, cancellationToken);
    }

    public ValueTask Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return NotificationWrappers.For(notification.GetType()).Publish(notification, services, notificationPublisher, cancellationToken);
    }
}

internal static class RequestWrappers<TResponse>
{
    private static readonly ConcurrentDictionary<Type, RequestWrapper<TResponse>> Cache = new();

    public static RequestWrapper<TResponse> For(Type requestType) =>
        Cache.GetOrAdd(requestType, static type =>
            (RequestWrapper<TResponse>)Activator.CreateInstance(typeof(RequestWrapper<,>).MakeGenericType(type, typeof(TResponse)))!);
}

internal abstract class RequestWrapper<TResponse>
{
    public abstract ValueTask<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class RequestWrapper<TRequest, TResponse> : RequestWrapper<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override ValueTask<TResponse> Handle(IRequest<TResponse> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
            ?? throw new InvalidOperationException(
                $"No handler is registered for {typeof(TRequest).FullName}. Implement IRequestHandler<{typeof(TRequest).Name}, {typeof(TResponse).Name}> " +
                "and add its assembly with AddCqrs(cqrs => cqrs.AddHandlersFromAssembly(...)), or register the handler yourself.");

        var typed = (TRequest)request;
        var behaviors = GetBehaviors(services);

        if (behaviors.Length == 0)
        {
            return Invoke(handler, typed, cancellationToken);
        }

        // Built inside out: the last behaviour wraps the handler, the first one wraps everything.
        RequestContinuation<TResponse> continuation = () => Invoke(handler, typed, cancellationToken);

        for (var index = behaviors.Length - 1; index >= 0; index--)
        {
            continuation = new Step(behaviors[index], typed, continuation, cancellationToken).Invoke;
        }

        return continuation();
    }

    private static ValueTask<TResponse> Invoke(IRequestHandler<TRequest, TResponse> handler, TRequest request, CancellationToken cancellationToken)
    {
        // The handler is named on the current span (the request's span when tracing is on), at no cost when nothing listens.
        if (Activity.Current is { IsAllDataRequested: true } activity)
        {
            activity.SetTag(Diagnostics.HandlerTag, handler.GetType().FullName);
        }

        return handler.Handle(request, cancellationToken);
    }

    // The container hands a collection of services over as an array, so this is not a copy.
    private static IPipelineBehavior<TRequest, TResponse>[] GetBehaviors(IServiceProvider services)
    {
        var behaviors = services.GetService<IEnumerable<IPipelineBehavior<TRequest, TResponse>>>();

        return behaviors switch
        {
            null => [],
            IPipelineBehavior<TRequest, TResponse>[] array => array,
            _ => [.. behaviors],
        };
    }

    private sealed class Step(IPipelineBehavior<TRequest, TResponse> behavior,
        TRequest request,
        RequestContinuation<TResponse> continuation,
        CancellationToken cancellationToken)
    {
        public ValueTask<TResponse> Invoke() => behavior.Handle(request, continuation, cancellationToken);
    }
}

internal static class NotificationWrappers
{
    private static readonly ConcurrentDictionary<Type, NotificationWrapper> Cache = new();

    public static NotificationWrapper For(Type notificationType) =>
        Cache.GetOrAdd(notificationType, static type =>
            (NotificationWrapper)Activator.CreateInstance(typeof(NotificationWrapper<>).MakeGenericType(type))!);
}

internal abstract class NotificationWrapper
{
    public abstract ValueTask Publish(INotification notification,
        IServiceProvider services,
        INotificationPublisher publisher,
        CancellationToken cancellationToken);
}

internal sealed class NotificationWrapper<TNotification> : NotificationWrapper
    where TNotification : INotification
{
    public override ValueTask Publish(INotification notification,
        IServiceProvider services,
        INotificationPublisher publisher,
        CancellationToken cancellationToken)
    {
        var resolved = services.GetService<IEnumerable<INotificationHandler<TNotification>>>();

        IReadOnlyList<INotificationHandler<TNotification>> handlers = resolved switch
        {
            null => [],
            IReadOnlyList<INotificationHandler<TNotification>> list => list,
            _ => [.. resolved],
        };

        return handlers.Count == 0
            ? ValueTask.CompletedTask
            : publisher.Publish(handlers, (TNotification)notification, cancellationToken);
    }
}
