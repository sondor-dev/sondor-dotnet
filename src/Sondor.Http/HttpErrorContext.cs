using System.Net.Http.Headers;
using System.Text.Json;
using Sondor.Http.Internal;

namespace Sondor.Http;

/// <summary>
/// A failed response, as an <see cref="IHttpErrorReader"/> sees it: the status and headers, and a body that is read once, bounded by
/// <see cref="HttpResultOptions.MaxErrorBodyBytes"/>, and shared by every reader. It is valid only during the call to the reader.
/// </summary>
public sealed class HttpErrorContext
{
    private readonly HttpResponseMessage _response;
    private readonly TimeProvider _time;
    private byte[]? _body;
    private bool _truncated;
    private bool _bodyRead;
    private JsonDocument? _json;
    private bool _jsonRead;

    internal HttpErrorContext(HttpResponseMessage response, string method, string host, HttpResultOptions options, TimeProvider time)
    {
        _response = response;
        _time = time;
        Status = (int)response.StatusCode;
        Method = method;
        Host = host;
        Options = options;
    }

    /// <summary>The status of the response.</summary>
    public int Status { get; }

    /// <summary>The method of the request.</summary>
    public string Method { get; }

    /// <summary>The host of the request, never its path or query.</summary>
    public string Host { get; }

    /// <summary>The settings of the client.</summary>
    public HttpResultOptions Options { get; }

    /// <summary>The response headers.</summary>
    public HttpResponseHeaders Headers => _response.Headers;

    /// <summary>The content headers: the media type and the length.</summary>
    public HttpContentHeaders ContentHeaders => _response.Content.Headers;

    /// <summary>Whether the body was longer than <see cref="HttpResultOptions.MaxErrorBodyBytes"/> and was cut. Valid after reading it.</summary>
    public bool BodyTruncated => _truncated;

    /// <summary>
    /// The body, at most <see cref="HttpResultOptions.MaxErrorBodyBytes"/> bytes of it. Empty when there is none. Read once; every
    /// later call returns the same bytes.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the call times out or the caller cancels.</param>
    /// <returns>The bytes of the body.</returns>
    public async ValueTask<ReadOnlyMemory<byte>> ReadBodyAsync(CancellationToken cancellationToken = default)
    {
        if (_bodyRead)
        {
            return _body;
        }

        var max = Options.MaxErrorBodyBytes;
        var length = _response.Content.Headers.ContentLength;

        if (length == 0 || max == 0)
        {
            _truncated = max == 0 && length != 0;
            _bodyRead = true;
            _body = [];

            return _body;
        }

        // The stream belongs to the response, which whoever made the call disposes: closing it here would break a second look at a
        // buffered body.
        var stream = await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[length is { } known ? (int)Math.Min(known, max) : Math.Min(max, 4096)];
        var total = 0;

        while (true)
        {
            if (total == buffer.Length)
            {
                if (buffer.Length == max)
                {
                    // Full: is there more? One byte answers it without keeping any.
                    var probe = new byte[1];

                    _truncated = await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) > 0;

                    break;
                }

                Array.Resize(ref buffer, (int)Math.Min((long)buffer.Length * 2, max));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        _body = total == buffer.Length ? buffer : buffer.AsSpan(0, total).ToArray();
        _bodyRead = true;

        return _body;
    }

    /// <summary>
    /// The body as a JSON document, or null when the response is not JSON (by its media type), the body was cut, or it does not
    /// parse. Nesting deeper than 32 levels does not parse. The document belongs to the context: do not dispose it.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the call times out or the caller cancels.</param>
    /// <returns>The document, or null.</returns>
    public async ValueTask<JsonDocument?> ReadJsonAsync(CancellationToken cancellationToken = default)
    {
        if (_jsonRead)
        {
            return _json;
        }

        if (!MediaTypes.IsJson(_response.Content.Headers.ContentType?.MediaType))
        {
            _jsonRead = true;

            return null;
        }

        var body = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);

        _jsonRead = true;

        if (_truncated || body.IsEmpty)
        {
            return null;
        }

        try
        {
            _json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            _json = null;
        }

        return _json;
    }

    /// <summary>
    /// The context of this failure, for <see cref="HttpErrorExtensions.GetHttpFailure(Error)"/>: status, method, host and the <c>Retry-After</c> header.
    /// </summary>
    /// <param name="problem">The problem document that was found, if any.</param>
    /// <param name="traceId">The trace id the other side reported, if any.</param>
    /// <returns>The failure.</returns>
    public HttpFailure CreateFailure(RemoteProblem? problem = null, string? traceId = null) =>
        new(Status, Method, Host, traceId, RetryAfter(), problem);

    /// <summary>
    /// Makes an error that carries the context of this failure. The code is used as given: check it if it came from the response.
    /// </summary>
    /// <param name="code">The stable code of the error.</param>
    /// <param name="description">The English description, safe to show.</param>
    /// <param name="type">The kind of failure.</param>
    /// <param name="problem">The problem document that was found, if any.</param>
    /// <param name="traceId">The trace id the other side reported, if any.</param>
    /// <returns>The error.</returns>
    public Error CreateError(string code, string description, ErrorType type, RemoteProblem? problem = null, string? traceId = null) =>
        ErrorFactory.Create(code, description, type, CreateFailure(problem, traceId));

    /// <summary>The error the package makes from the status alone: its code and type come from the options.</summary>
    /// <returns>The error.</returns>
    public Error CreateStatusError() =>
        CreateError(Options.CodeFor(Status), ErrorFactory.DescribeStatus(Status), Options.TypeFor(Status));

    internal void Release() => _json?.Dispose();

    private TimeSpan? RetryAfter()
    {
        var header = _response.Headers.RetryAfter;

        if (header?.Delta is { } delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (header?.Date is { } date)
        {
            var wait = date - _time.GetUtcNow();

            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }
}
