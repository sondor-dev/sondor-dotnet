using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Sondor.AspNetCore;

/// <summary>The words of a problem for a person: what an <see cref="IProblemTextProvider"/> returns.</summary>
/// <param name="Title">A short summary of the kind of problem.</param>
/// <param name="Detail">An explanation of this occurrence, safe to show to the caller.</param>
public readonly record struct ProblemText(string Title, string Detail);

/// <summary>
/// Supplies the <c>title</c> and <c>detail</c> of a problem, for an application that wants them in the reader's language or worded its
/// own way. The package registers none, so an application that does not need one pays nothing. Register one and every error
/// reported through this package uses it.
/// </summary>
public interface IProblemTextProvider
{
    /// <summary>The text of an error for a request.</summary>
    /// <param name="failure">The error being reported.</param>
    /// <param name="context">The request, for the reader's culture and anything else the text depends on.</param>
    /// <returns>The text, or null to use the error's own English text.</returns>
    ProblemText? GetText(Error failure, HttpContext context);
}

/// <summary>What an <see cref="IProblemContributor"/> sees of a problem that is being built.</summary>
public sealed class ProblemContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="problem">The problem, still open to changes.</param>
    /// <param name="error">The error the problem reports.</param>
    /// <param name="exception">The exception the error was made from, or null when it came from a failed <c>Result</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> or <paramref name="problem"/> is null.</exception>
    public ProblemContext(HttpContext httpContext, ProblemDetails problem, Error error, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(problem);

        HttpContext = httpContext;
        Problem = problem;
        Error = error;
        Exception = exception;
    }

    /// <summary>The request.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The problem, which a contributor adds members to through <see cref="ProblemDetails.Extensions"/>.</summary>
    public ProblemDetails Problem { get; }

    /// <summary>The error the problem reports.</summary>
    public Error Error { get; }

    /// <summary>The exception the error was made from, or null when it came from a failed <c>Result</c>.</summary>
    public Exception? Exception { get; }
}

/// <summary>
/// Adds members to a problem, such as the per-property <c>errors</c> of a validation failure. Register as many as you need; each runs
/// for every problem that is built from an <see cref="Error"/>, in the order registered, after the package has filled in its own
/// members.
/// </summary>
public interface IProblemContributor
{
    /// <summary>Adds members to the problem.</summary>
    /// <param name="context">The problem and what it was made from.</param>
    void Contribute(ProblemContext context);
}

/// <summary>Builds the problem response for an <see cref="Error"/>. Replace it to change how every error is reported.</summary>
public interface IErrorProblemFactory
{
    /// <summary>Creates the problem for an error.</summary>
    /// <param name="failure">The error to report.</param>
    /// <param name="context">The request being answered.</param>
    /// <param name="exception">The exception the error was made from, or null when it came from a failed <c>Result</c>.</param>
    /// <returns>The problem, not yet written.</returns>
    ProblemDetails Create(Error failure, HttpContext context, Exception? exception = null);
}
