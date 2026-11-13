using Microsoft.AspNetCore.Http;
using Sondor.AspNetCore.Internal;

namespace Sondor.AspNetCore;

/// <summary>
/// How errors are reported. Every setting has a default, so <c>AddSondorProblemDetails()</c> alone is a valid setup. The options use
/// the standard options pattern: set them in the delegate, or bind them with
/// <c>services.Configure&lt;ErrorResponseOptions&gt;(configuration.GetSection(...))</c>.
/// </summary>
public sealed class ErrorResponseOptions
{
    /// <summary>
    /// The status each <see cref="ErrorType"/> is answered with. The defaults are listed here so they can be seen and changed:
    /// <c>options.StatusByType[ErrorType.Validation] = 422;</c>. Every status must be between 400 and 599.
    /// </summary>
    public IDictionary<ErrorType, int> StatusByType { get; } = new Dictionary<ErrorType, int>
    {
        [ErrorType.Validation] = StatusCodes.Status400BadRequest,
        [ErrorType.Unauthorized] = StatusCodes.Status401Unauthorized,
        [ErrorType.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorType.NotFound] = StatusCodes.Status404NotFound,
        [ErrorType.Conflict] = StatusCodes.Status409Conflict,
        [ErrorType.RateLimited] = StatusCodes.Status429TooManyRequests,
        [ErrorType.FeatureDisabled] = StatusCodes.Status501NotImplemented,
        [ErrorType.InternalServerError] = StatusCodes.Status500InternalServerError,
        [ErrorType.Failure] = StatusCodes.Status500InternalServerError,
    };

    /// <summary>
    /// The status of one specific error code, which wins over <see cref="StatusByType"/>. Empty by default. Every status must be
    /// between 400 and 599.
    /// </summary>
    public IDictionary<string, int> StatusByCode { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// The <c>errorCode</c> given to a problem the framework produced on its own (a bare 404, a 415, an invalid body), which has no
    /// <see cref="Error"/>, so every response carries a code. A status that is not listed gets <c>Request.Failed</c>, or
    /// <c>Error.Internal</c> from 500 up.
    /// </summary>
    public IDictionary<int, string> CodeByStatus { get; } = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = "Request.Invalid",
        [StatusCodes.Status401Unauthorized] = "Request.Unauthorized",
        [StatusCodes.Status403Forbidden] = "Request.Forbidden",
        [StatusCodes.Status404NotFound] = "Request.NotFound",
        [StatusCodes.Status409Conflict] = "Request.Conflict",
        [StatusCodes.Status415UnsupportedMediaType] = "Request.UnsupportedMediaType",
        [StatusCodes.Status429TooManyRequests] = "Request.RateLimited",
        [StatusCodes.Status499ClientClosedRequest] = "Request.Cancelled",
    };

    /// <summary>
    /// A link to the documentation of an error code, used as the <c>type</c> of the problem. <c>{code}</c> is replaced by the code,
    /// escaped for a URL: <c>https://docs.example.com/errors/{code}</c>. Null (the default) keeps the framework's <c>type</c>, a link
    /// to the HTTP status.
    /// </summary>
    public string? TypeUriTemplate { get; set; }

    /// <summary>The name of the member that holds the error code. <c>errorCode</c> by default.</summary>
    public string ErrorCodeMember { get; set; } = "errorCode";

    /// <summary>
    /// Answer an exception that escapes a request with a problem response. On by default; turn it off to keep the application's own
    /// exception handling.
    /// </summary>
    public bool HandleExceptions { get; set; } = true;

    /// <summary>
    /// Give a bare error status (a 401 or a 404 with no body) a problem body, as the ASP.NET Core documentation recommends. On by
    /// default. It applies to <c>UseSondorProblemDetails()</c>; an application that adds the status code pages itself is unaffected.
    /// </summary>
    public bool WriteStatusCodeBodies { get; set; } = true;

    /// <summary>
    /// Put the message of an unexpected exception in <c>detail</c>, with its type in an <c>exception</c> member. Off by default: an
    /// exception message can hold paths, queries and secrets. Meant for development only.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Use the trace id alone as <c>traceId</c>, which can be pasted into a trace viewer. Off by default, which keeps the framework's
    /// value (the whole W3C <c>traceparent</c>: version, trace id, span id and flags).
    /// </summary>
    public bool BareTraceId { get; set; }

    /// <summary>
    /// Tag the request's <see cref="System.Diagnostics.Activity"/> with <c>error.type</c> (the error code) when a problem is written,
    /// and mark it as failed from status 500 up. On by default.
    /// </summary>
    public bool TagActivity { get; set; } = true;

    /// <summary>
    /// The status for a request whose caller disconnected before the answer was ready. It is not an HTTP standard, but is widely used
    /// (nginx). 499 by default; such a request is never logged as an error.
    /// </summary>
    public int ClientClosedRequestStatus { get; set; } = StatusCodes.Status499ClientClosedRequest;

    /// <summary>
    /// Which requests get a problem response from the exception handler and the status code bodies. The rest are left alone, so an
    /// application with its own error pages for Razor Pages and views keeps them. By default a request is an API request when its
    /// endpoint is an <c>[ApiController]</c> action or a minimal API, and is not when it is a Razor Page or a view-returning controller;
    /// when there is no endpoint yet, it is one unless the <c>Accept</c> header prefers HTML.
    /// </summary>
    public Func<HttpContext, bool> AppliesTo { get; set; } = RequestClassifier.IsApiRequest;

    /// <summary>The exceptions that are answered with a specific error. See <see cref="ExceptionMappings.Map{TException}"/>.</summary>
    public ExceptionMappings Exceptions { get; } = new();
}
