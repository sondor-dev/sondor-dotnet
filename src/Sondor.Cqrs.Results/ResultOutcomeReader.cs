using System.Reflection;
using Sondor.Results;

namespace Sondor.Cqrs;

/// <summary>
/// Tells tracing, metrics and logging that a failed <c>Result</c> is a failure, and which error code it carries. Registered for
/// every response type, because the container throws, rather than skips, when a response type is asked for that breaks a
/// generic constraint: a response that is not a result reads as a success, as it does with no reader at all.
/// </summary>
internal sealed class ResultOutcomeReader<TResponse> : IOutcomeReader<TResponse>
{
    // Worked out once for each response type. A delegate to a constrained generic method, so a struct result is not boxed to read it.
    private static readonly Func<TResponse, RequestOutcome>? Reader = Build();

    public RequestOutcome Read(TResponse response) => Reader is null ? RequestOutcome.Success : Reader(response);

    private static Func<TResponse, RequestOutcome>? Build() =>
        typeof(IResultState).IsAssignableFrom(typeof(TResponse))
            ? typeof(ResultOutcomeReader<TResponse>)
                .GetMethod(nameof(ReadState), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(typeof(TResponse))
                .CreateDelegate<Func<TResponse, RequestOutcome>>()
            : null;

    private static RequestOutcome ReadState<TState>(TState state)
        where TState : IResultState =>
        state is null || state.IsSuccess ? RequestOutcome.Success : RequestOutcome.Failure(state.Error.Code);
}
