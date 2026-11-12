using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sondor.Cqrs.Internal;

namespace Sondor.Cqrs.Behaviors;

/// <summary>The settings of the logging behaviour, set by <see cref="CqrsBuilder.UseLogging"/>.</summary>
internal sealed record LoggingSettings(TimeSpan SlowRequestThreshold);

/// <summary>
/// Logs every request: what it was, how long it took and how it ended. Never the request itself: a request can hold a
/// password or a token, and a log is read by far more people than the code is.
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILoggerFactory loggerFactory,
    LoggingSettings settings,
    IOutcomeReader<TResponse>? outcomeReader = null) :
    IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(CqrsTelemetry.SourceName);

    public async ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            var response = await continuation().ConfigureAwait(false);

            var elapsed = Stopwatch.GetElapsedTime(started);
            var outcome = outcomeReader?.Read(response) ?? RequestOutcome.Success;

            if (elapsed > settings.SlowRequestThreshold)
            {
                Slow(_logger, RequestKind<TRequest>.Value, typeof(TRequest).Name, elapsed.TotalMilliseconds, settings.SlowRequestThreshold.TotalMilliseconds);
            }
            else if (outcome.IsSuccess)
            {
                Handled(_logger, RequestKind<TRequest>.Value, typeof(TRequest).Name, elapsed.TotalMilliseconds);
            }
            else
            {
                Failed(_logger, RequestKind<TRequest>.Value, typeof(TRequest).Name, outcome.ErrorCode ?? "(no code)", elapsed.TotalMilliseconds);
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            Cancelled(_logger, RequestKind<TRequest>.Value, typeof(TRequest).Name, elapsed);
            throw;
        }
        catch (Exception exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            // The type only: whoever catches the exception logs it whole, so it is not logged twice.
            Threw(_logger, RequestKind<TRequest>.Value, typeof(TRequest).Name, exception.GetType().Name, elapsed);
            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Handled {Kind} {Request} in {ElapsedMs:0.0} ms")]
    private static partial void Handled(ILogger logger, string kind, string request, double elapsedMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Handled {Kind} {Request} in {ElapsedMs:0.0} ms, over the {ThresholdMs:0} ms threshold")]
    private static partial void Slow(ILogger logger, string kind, string request, double elapsedMs, double thresholdMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Refused {Kind} {Request} with {ErrorCode} in {ElapsedMs:0.0} ms")]
    private static partial void Failed(ILogger logger, string kind, string request, string errorCode, double elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Cancelled {Kind} {Request} after {ElapsedMs:0.0} ms")]
    private static partial void Cancelled(ILogger logger, string kind, string request, double elapsedMs);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "{Kind} {Request} threw {ExceptionType} after {ElapsedMs:0.0} ms")]
    private static partial void Threw(ILogger logger, string kind, string request, string exceptionType, double elapsedMs);
}
