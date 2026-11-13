using System.Diagnostics;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs.Behaviors;

/// <summary>A span around every request. With nothing listening it does nothing at all.</summary>
internal sealed class TracingBehavior<TRequest, TResponse>(IOutcomeReader<TResponse>? outcomeReader = null) :
    IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken) =>
        Diagnostics.Source.HasListeners()
            ? Traced(continuation, cancellationToken)
            : continuation();

    private async ValueTask<TResponse> Traced(RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
    {
        // Started in this async method, not in the caller of it: Activity.Current is an async local, and a change made inside an
        // async method is undone when the method returns to its caller. Started outside, the span is left current after it ends,
        // and every later request becomes a child of the one before.
        using var activity = Diagnostics.Source.StartActivity(typeof(TRequest).Name);

        if (activity is null)
        {
            // Something listens, but not to this span.
            return await continuation().ConfigureAwait(false);
        }

        activity.SetTag(Diagnostics.RequestTag, typeof(TRequest).FullName);
        activity.SetTag(Diagnostics.KindTag, RequestKind<TRequest>.Value);

        try
        {
            var response = await continuation().ConfigureAwait(false);

            var outcome = outcomeReader?.Read(response) ?? RequestOutcome.Success;

            if (outcome.IsSuccess)
            {
                activity.SetTag(Diagnostics.OutcomeTag, Diagnostics.Success);
            }
            else
            {
                activity.SetTag(Diagnostics.OutcomeTag, Diagnostics.Failure);
                activity.SetTag(Diagnostics.ErrorCodeTag, outcome.ErrorCode);
                activity.SetTag(Diagnostics.ErrorTypeTag, outcome.ErrorCode ?? Diagnostics.Failure);
                activity.SetStatus(ActivityStatusCode.Error, outcome.ErrorCode);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity.SetTag(Diagnostics.OutcomeTag, Diagnostics.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            activity.SetTag(Diagnostics.OutcomeTag, Diagnostics.Error);
            activity.SetTag(Diagnostics.ErrorTypeTag, exception.GetType().FullName);
            activity.AddException(exception);
            activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }
}
