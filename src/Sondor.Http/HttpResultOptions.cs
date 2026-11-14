using System.Text.Json;

namespace Sondor.Http;

/// <summary>
/// The settings of one named client. Every setting has a default, so a client needs none of them. They are validated when the host
/// starts.
/// </summary>
/// <remarks>
/// The plain values (<see cref="BaseAddress"/>, the limits, the maps) bind from configuration with
/// <c>services.Configure&lt;HttpResultOptions&gt;("orders", section)</c>; the section name is yours. The error readers, the JSON options
/// and the exception mappings are set in code.
/// </remarks>
public sealed class HttpResultOptions
{
    private static readonly (int Status, string Code)[] DefaultCodes =
    [
        (400, "Request.Invalid"),
        (401, "Request.Unauthorized"),
        (403, "Request.Forbidden"),
        (404, "Request.NotFound"),
        (409, "Request.Conflict"),
        (415, "Request.UnsupportedMediaType"),
        (422, "Request.Invalid"),
        (429, "Request.RateLimited"),
        (499, "Request.Cancelled"),
        (501, "Feature.Disabled"),
        (500, "Error.Internal"),
        (503, "Request.Unavailable"),
        (504, "Request.GatewayTimeout")
    ];

    private static readonly (int Status, ErrorType Type)[] DefaultTypes =
    [
        (400, ErrorType.Validation),
        (401, ErrorType.Unauthorized),
        (403, ErrorType.Forbidden),
        (404, ErrorType.NotFound),
        (409, ErrorType.Conflict),
        (422, ErrorType.Validation),
        (429, ErrorType.RateLimited),
        (500, ErrorType.InternalServerError),
        (501, ErrorType.FeatureDisabled),
        (502, ErrorType.Failure),
        (503, ErrorType.Failure),
        (504, ErrorType.Failure)
    ];

    /// <summary>
    /// Creates the options with their defaults.
    /// </summary>
    public HttpResultOptions()
    {
        CodeByStatus = new Dictionary<int, string>(DefaultCodes.Length);
        TypeByStatus = new Dictionary<int, ErrorType>(DefaultTypes.Length);

        foreach (var (status, code) in DefaultCodes)
        {
            CodeByStatus[status] = code;
        }

        foreach (var (status, type) in DefaultTypes)
        {
            TypeByStatus[status] = type;
        }
    }

    /// <summary>
    /// The address every relative path is resolved against. Applied to the <see cref="HttpClient"/> of a client registered with
    /// <c>AddSondorHttpClient</c>; a client built by hand uses the address of the <see cref="HttpClient"/> it is given. Must be an
    /// absolute URI. Default: none.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The longest one call may take, from sending the request to having read the body, unless a call sets its own. When it passes
    /// the call fails with <c>Http.Timeout</c>. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> turns it off. Default: 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The largest body of a successful response, in bytes, after decompression. A larger one is not read and the call fails with
    /// <c>Http.TooLarge</c>. Default: 10 MiB.
    /// </summary>
    public long MaxResponseBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// The largest error body that is read at all, in bytes. A larger one is cut and is not parsed; the error then comes from the
    /// status alone. Default: 64 KiB.
    /// </summary>
    public int MaxErrorBodyBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// How bodies are read and written. Default: <see cref="JsonSerializerDefaults.Web"/>. The options become read-only on first use.
    /// </summary>
    public JsonSerializerOptions Json { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The member of a problem document that holds the stable error code. It is the member <c>Sondor.AspNetCore</c> writes.
    /// Default: <c>errorCode</c>.
    /// </summary>
    public string ErrorCodeMember { get; set; } = "errorCode";

    /// <summary>
    /// The code of an error whose response has no usable code of its own, for each status. It is the same vocabulary the server side
    /// of this package family uses. A status that is not listed gets <c>Request.Failed</c>, or <c>Error.Internal</c> from 500.
    /// </summary>
    public IDictionary<int, string> CodeByStatus { get; }

    /// <summary>
    /// The <see cref="ErrorType"/> of an error for each status. A status that is not listed is <see cref="ErrorType.Failure"/>, or
    /// <see cref="ErrorType.InternalServerError"/> from 500.
    /// </summary>
    public IDictionary<int, ErrorType> TypeByStatus { get; }

    /// <summary>
    /// Whether the <c>detail</c> of a problem document is used as the <see cref="Error.Description"/>. Off by default: the other
    /// side's words are not yours, and a description is meant to be safe to show. They stay available on
    /// <see cref="RemoteProblem.Detail"/>. When on, the text is trimmed, control characters are removed and it is cut to
    /// <see cref="MaxRemoteDetailLength"/>.
    /// </summary>
    public bool UseRemoteDetail { get; set; }

    /// <summary>The longest remote <c>detail</c> used as a description, in characters. Default: 256.</summary>
    public int MaxRemoteDetailLength { get; set; } = 256;

    /// <summary>
    /// Whether the client logs each call: debug for a success, information for a 4xx, warning for anything else. A line holds the
    /// method, the host, the status, the elapsed time and the error code, and never a path, a query string, a header or a body.
    /// Default: on.
    /// </summary>
    public bool LogCalls { get; set; } = true;

    /// <summary>
    /// Readers for error formats other than RFC 9457, asked in order for every failed response. The first to return an error wins;
    /// when none does, the problem document is read, then the status alone. A reader is shared by every call of the client, so it
    /// must be safe to use from several threads.
    /// </summary>
    public IList<IHttpErrorReader> ErrorReaders { get; } = [];

    /// <summary>
    /// The exceptions that are reported as an <see cref="Error"/> instead of being thrown. An unreachable host, a timeout and an
    /// interrupted body are mapped already; add the exceptions of a resilience pipeline here. The caller's own cancellation is never
    /// mapped.
    /// </summary>
    public HttpExceptionMappings Exceptions { get; } = new();

    internal string CodeFor(int status)
    {
        if (CodeByStatus.TryGetValue(status, out var code))
        {
            return code;
        }

        return status >= 500 ? "Error.Internal" : "Request.Failed";
    }

    internal ErrorType TypeFor(int status)
    {
        if (TypeByStatus.TryGetValue(status, out var type))
        {
            return type;
        }

        return status >= 500 ? ErrorType.InternalServerError : ErrorType.Failure;
    }
}
