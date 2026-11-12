namespace Sondor.Cqrs;

/// <summary>Sends a request to its handler, through the pipeline.</summary>
public interface ISender
{
    /// <summary>Sends the request and returns the handler's response.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the response.</param>
    /// <returns>The response.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the request's type.</exception>
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>Publishes a notification to every handler registered for its type.</summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes the notification to the handlers of its runtime type, so a notification published through a base reference
    /// reaches the handlers of the type it really is. With no handler nothing happens.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Signals that the publisher no longer needs the handlers to finish.</param>
    ValueTask Publish(INotification notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// The rest of a request's pipeline, from the point of view of a behaviour: calling it runs the next behaviour, or the handler
/// when there is none left. A behaviour may call it once, several times (a retry) or not at all (a refusal).
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <returns>The response.</returns>
public delegate ValueTask<TResponse> RequestContinuation<TResponse>();

/// <summary>
/// Code that runs around every request of a kind: tracing, validation, caching, auditing. Behaviours run in the order they were
/// added, the first one outermost.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request, normally by calling <paramref name="continuation"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="continuation">The rest of the pipeline.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the response.</param>
    /// <returns>The response.</returns>
    ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken);
}

/// <summary>Decides how the handlers of a notification run: one after another, all at once, or some other way.</summary>
public interface INotificationPublisher
{
    /// <summary>Runs the handlers.</summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="handlers">The handlers, in registration order. Never empty.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Signals that the publisher no longer needs the handlers to finish.</param>
    ValueTask Publish<TNotification>(IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification;
}
