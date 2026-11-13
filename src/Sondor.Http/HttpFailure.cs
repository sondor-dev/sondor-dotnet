namespace Sondor.Http;

/// <summary>
/// What is known about a failed call beyond its code: the status, the other side's trace id, a <c>Retry-After</c>, the problem
/// document. It is kept on the <see cref="Error"/> (see <see cref="HttpErrorExtensions.GetHttpFailure"/>) and is immutable.
/// </summary>
/// <remarks>
/// The members that came from the other side are untrusted text: do not put them in a response or a page unchecked. They are bounded
/// in size. <see cref="ToString"/> shows the status only, so logging an error cannot leak any of it.
/// </remarks>
public sealed class HttpFailure
{
    internal const string ArgumentName = "Http.Failure";

    internal HttpFailure(int? status, string method, string host, string? traceId, TimeSpan? retryAfter, RemoteProblem? problem)
    {
        Status = status;
        Method = method;
        Host = host;
        TraceId = traceId;
        RetryAfter = retryAfter;
        Problem = problem;
    }

    /// <summary>The status of the response, or null when there was none (the host was not reachable, the call timed out).</summary>
    public int? Status { get; }

    /// <summary>The method of the request, such as <c>GET</c>.</summary>
    public string Method { get; }

    /// <summary>The host of the request, with the port when it is not the default one. Never the path or the query.</summary>
    public string Host { get; }

    /// <summary>The trace id the other side reported in its problem document, to quote when reporting a problem. Untrusted text.</summary>
    public string? TraceId { get; }

    /// <summary>When to try again, from a <c>Retry-After</c> header (a number of seconds or a date), or null.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The problem document the other side sent, or null when it sent none.</summary>
    public RemoteProblem? Problem { get; }

    /// <inheritdoc />
    public override string ToString() => Status is { } status ? $"HttpFailure (status {status})" : "HttpFailure (no response)";
}

/// <summary>
/// A problem document of another service (RFC 9457), read with bounded sizes. Every text is untrusted: it is the other side's
/// words. Immutable.
/// </summary>
public sealed class RemoteProblem
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyErrors =
        new Dictionary<string, IReadOnlyList<string>>(0);

    /// <summary>Creates a problem, for a reader of another error format that wants to keep what it found.</summary>
    /// <param name="type">The <c>type</c> member.</param>
    /// <param name="title">The <c>title</c> member.</param>
    /// <param name="status">The <c>status</c> member.</param>
    /// <param name="detail">The <c>detail</c> member.</param>
    /// <param name="instance">The <c>instance</c> member.</param>
    /// <param name="errorCode">The error code the other side reported, as it sent it.</param>
    /// <param name="errors">Messages by property name.</param>
    public RemoteProblem(
        string? type = null,
        string? title = null,
        int? status = null,
        string? detail = null,
        string? instance = null,
        string? errorCode = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null)
    {
        Type = type;
        Title = title;
        Status = status;
        Detail = detail;
        Instance = instance;
        ErrorCode = errorCode;
        Errors = errors ?? EmptyErrors;
    }

    /// <summary>The <c>type</c> member: a URI that names the kind of problem.</summary>
    public string? Type { get; }

    /// <summary>The <c>title</c> member.</summary>
    public string? Title { get; }

    /// <summary>The <c>status</c> member, as the other side wrote it.</summary>
    public int? Status { get; }

    /// <summary>The <c>detail</c> member.</summary>
    public string? Detail { get; }

    /// <summary>The <c>instance</c> member.</summary>
    public string? Instance { get; }

    /// <summary>The error code as the other side sent it, which may not be a valid code. The error's own code is checked.</summary>
    public string? ErrorCode { get; }

    /// <summary>The <c>errors</c> member: messages by property name. Empty when there are none.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Errors { get; }

    /// <inheritdoc />
    public override string ToString() => "RemoteProblem";
}

/// <summary>Reads what <see cref="HttpFailure"/> an <see cref="Error"/> carries.</summary>
public static class HttpErrorExtensions
{
    /// <summary>
    /// The <see cref="HttpFailure"/> of an error that came from a call of this package, or null for any other error.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <returns>The failure, or null.</returns>
    public static HttpFailure? GetHttpFailure(this Error error) =>
        error.Arguments.TryGetValue(HttpFailure.ArgumentName, out var argument) ? argument.Value as HttpFailure : null;
}
