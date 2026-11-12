using System.Diagnostics;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs.Behaviors;

/// <summary>Counts and times every request. With nothing listening it does nothing at all.</summary>
internal sealed class MetricsBehavior<TRequest, TResponse>(IOutcomeReader<TResponse>? outcomeReader = null) :
    IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken) =>
        Diagnostics.Requests.Enabled || Diagnostics.Duration.Enabled
            ? Measured(continuation, cancellationToken)
            : continuation();

    private async ValueTask<TResponse> Measured(RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = Diagnostics.Error;

        try
        {
            var response = await continuation().ConfigureAwait(false);

            outcome = (outcomeReader?.Read(response) ?? RequestOutcome.Success).IsSuccess ? Diagnostics.Success : Diagnostics.Failure;

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = Diagnostics.Cancelled;
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { Diagnostics.RequestTag, typeof(TRequest).Name },
                { Diagnostics.KindTag, RequestKind<TRequest>.Value },
                { Diagnostics.OutcomeTag, outcome },
            };

            Diagnostics.Requests.Add(1, tags);
            Diagnostics.Duration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }
}
