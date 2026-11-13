using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore;

/// <summary>Turns a <see cref="Result"/>, or an <see cref="Error"/>, into an HTTP response.</summary>
public static class ResultHttpExtensions
{
    /// <summary>
    /// A problem response for an error, written the same way as the failure of a result. For code that has no result to return: a
    /// middleware that rejects a request (a missing header, a blocked client) writes
    /// <c>await error.ToHttpResult().ExecuteAsync(context);</c> and does not call the next middleware.
    /// </summary>
    /// <param name="error">The error to report.</param>
    /// <returns>The response.</returns>
    public static IResult ToHttpResult(this Error error) => new ErrorProblemResult(error);

    /// <summary>
    /// A success is <c>204 No Content</c>; a failure is a problem response (see <see cref="IErrorProblemFactory"/>). The result works
    /// from a minimal API and from an MVC action, which may return an <see cref="IResult"/>.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The response.</returns>
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : new ErrorProblemResult(result.Error);

    /// <summary>
    /// A success is <c>200 OK</c> with the value, or whatever <paramref name="onSuccess"/> makes of it (a <c>201 Created</c>, a
    /// redirect); a failure is a problem response (see <see cref="IErrorProblemFactory"/>). The result works from a minimal API and
    /// from an MVC action, which may return an <see cref="IResult"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Makes the response for a value, instead of <c>200 OK</c>.</param>
    /// <returns>The response.</returns>
    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null)
    {
        if (result.IsFailure)
        {
            return new ErrorProblemResult(result.Error);
        }

        return onSuccess is null ? TypedResults.Ok(result.Value) : onSuccess(result.Value);
    }

    private sealed class ErrorProblemResult(Error error) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var factory = httpContext.RequestServices.GetService<IErrorProblemFactory>() ??
                          throw new InvalidOperationException("Call AddSondorProblemDetails() to register the error handling this application uses.");

            // Through the framework's own problem result, so the problem is written like every other: negotiated, customized and traced.
            return TypedResults.Problem(factory.Create(error, httpContext)).ExecuteAsync(httpContext);
        }
    }
}
