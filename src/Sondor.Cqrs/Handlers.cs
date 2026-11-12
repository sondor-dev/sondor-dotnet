namespace Sondor.Cqrs;

/// <summary>Handles one kind of request. There is exactly one handler for each request type.</summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the response.</param>
    /// <returns>The response.</returns>
    ValueTask<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Something that happened. Any number of handlers, including none, react to it.</summary>
#pragma warning disable CA1040 // Avoid empty interfaces: a marker, so only notifications can be published.
public interface INotification;
#pragma warning restore CA1040

/// <summary>Reacts to one kind of notification.</summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Signals that the publisher no longer needs the handlers to finish.</param>
    ValueTask Handle(TNotification notification, CancellationToken cancellationToken);
}
