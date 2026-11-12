namespace Sondor.Cqrs;

/// <summary>
/// Runs the handlers one after another in registration order. The first handler that throws stops the rest and the exception
/// reaches the publisher. This is the default.
/// </summary>
public sealed class SequentialNotificationPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async ValueTask Publish<TNotification>(IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);

        for (var index = 0; index < handlers.Count; index++)
        {
            await handlers[index].Handle(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Starts every handler at once and waits for all of them. Every handler runs even when another throws; the exceptions are
/// then thrown together as an <see cref="AggregateException"/> (a single failure is thrown as it is).
/// </summary>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async ValueTask Publish<TNotification>(IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var tasks = new Task[handlers.Count];

        for (var index = 0; index < tasks.Length; index++)
        {
            tasks[index] = Start(handlers[index], notification, cancellationToken);
        }

        var all = Task.WhenAll(tasks);

        try
        {
            await all.ConfigureAwait(false);
        }
        catch (Exception) when (all.Exception is { InnerExceptions.Count: > 1 } aggregate)
        {
            throw aggregate;
        }
    }

    // Task.Run is not used: a handler's synchronous part runs on the caller's thread, as it does in the sequential publisher.
    private static async Task Start<TNotification>(INotificationHandler<TNotification> handler,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification =>
        await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
}
