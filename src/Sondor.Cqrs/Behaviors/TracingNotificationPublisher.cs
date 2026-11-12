using System.Diagnostics;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs.Behaviors;

/// <summary>Wraps a publisher: a span for the notification and a child span for each handler, named after the handler class.</summary>
internal sealed class TracingNotificationPublisher(INotificationPublisher inner) : INotificationPublisher
{
    public async ValueTask Publish<TNotification>(IReadOnlyList<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        using var activity = Diagnostics.Source.StartActivity(typeof(TNotification).Name);

        if (activity is null)
        {
            await inner.Publish(handlers, notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        activity.SetTag(Diagnostics.RequestTag, typeof(TNotification).FullName);
        activity.SetTag(Diagnostics.KindTag, "notification");

        var traced = new INotificationHandler<TNotification>[handlers.Count];

        for (var index = 0; index < traced.Length; index++)
        {
            traced[index] = new TracedHandler<TNotification>(handlers[index]);
        }

        try
        {
            await inner.Publish(traced, notification, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            activity.SetTag(Diagnostics.ErrorTypeTag, exception.GetType().FullName);
            activity.AddException(exception);
            activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }

    private sealed class TracedHandler<TNotification>(INotificationHandler<TNotification> handler) : INotificationHandler<TNotification>
        where TNotification : INotification
    {
        public async ValueTask Handle(TNotification notification, CancellationToken cancellationToken)
        {
            using var activity = Diagnostics.Source.StartActivity(handler.GetType().Name);

            try
            {
                await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (activity is not null && (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                activity.SetTag(Diagnostics.ErrorTypeTag, exception.GetType().FullName);
                activity.AddException(exception);
                activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                throw;
            }
        }
    }
}
