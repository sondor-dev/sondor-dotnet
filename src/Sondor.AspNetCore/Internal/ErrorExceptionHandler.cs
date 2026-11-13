using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>
/// Answers an exception that escapes an API request: it is mapped to an <see cref="Error"/> and reported like any other. Whatever is
/// not mapped becomes a generic 500; the exception is logged and never sent.
/// </summary>
internal sealed partial class ErrorExceptionHandler(
    IOptions<ErrorResponseOptions> options,
    ILogger<ErrorExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Error Cancelled = new("Request.Cancelled", "The request was cancelled.", ErrorType.Failure);

    private static readonly Error Unreadable = new("Request.Invalid", "The request could not be read.", ErrorType.Validation);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.HandleExceptions || !settings.AppliesTo(httpContext))
        {
            return false;
        }

        exception = Unwrap(exception);

        var (error, status, mapped) = Resolve(exception, httpContext, settings);

        var factory = httpContext.RequestServices.GetService<IErrorProblemFactory>() ??
                      throw new InvalidOperationException("Call AddSondorProblemDetails() to register the error handling this application uses.");

        var problem = factory.Create(error, httpContext, exception);

        if (status is { } overridden)
        {
            problem.Status = overridden;
        }

        if (settings.IncludeExceptionDetails && !mapped)
        {
            problem.Detail = exception.Message;
            problem.Extensions["exception"] = exception.GetType().FullName;
        }

        Log(exception, error, problem.Status ?? StatusCodes.Status500InternalServerError, cancelled: error == Cancelled);

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        var service = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        var written = await service.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        if (!written)
        {
            // The caller did not accept problem+json (an Accept header that names something else): answer in JSON anyway.
            await TypedResults.Problem(problem).ExecuteAsync(httpContext);
        }

        return true;
    }

    private static (Error Error, int? Status, bool Mapped) Resolve(Exception exception, HttpContext context, ErrorResponseOptions settings)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return (Cancelled, settings.ClientClosedRequestStatus, true);
        }

        if (settings.Exceptions.TryMap(exception, out var mapped))
        {
            return (mapped, null, true);
        }

        if (exception is BadHttpRequestException bad)
        {
            return (Unreadable, bad.StatusCode, true);
        }

        return (Error.Internal("process the request"), null, false);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (true)
        {
            switch (exception)
            {
                case AggregateException { InnerExceptions.Count: 1 } aggregate:
                    exception = aggregate.InnerExceptions[0];
                    break;
                case TargetInvocationException { InnerException: { } inner }:
                    exception = inner;
                    break;
                default:
                    return exception;
            }
        }
    }

    private void Log(Exception exception, Error error, int status, bool cancelled)
    {
        if (cancelled)
        {
            RequestCancelled(logger);
        }
        else if (status >= StatusCodes.Status500InternalServerError)
        {
            ServerError(logger, exception, status, error.Code);
        }
        else
        {
            ClientError(logger, exception, status, error.Code);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "An exception was answered with {Status} ({ErrorCode}).")]
    private static partial void ServerError(ILogger logger, Exception exception, int status, string errorCode);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "An exception was answered with {Status} ({ErrorCode}).")]
    private static partial void ClientError(ILogger logger, Exception exception, int status, string errorCode);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "The caller closed the request before it was answered.")]
    private static partial void RequestCancelled(ILogger logger);
}
