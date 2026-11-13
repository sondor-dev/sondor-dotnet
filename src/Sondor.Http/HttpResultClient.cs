using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sondor.Http.Internal;

namespace Sondor.Http;

/// <summary>
/// Calls another HTTP service and returns the answer as a <see cref="Result"/> or <see cref="Result{T}"/>. A response that is not a
/// success, a problem document, a timeout and an unreachable host are failed results, never exceptions. The caller's own cancellation
/// is not a failure: it throws <see cref="OperationCanceledException"/>, as everywhere in .NET.
/// </summary>
/// <remarks>
/// Nothing here changes the shared <see cref="HttpClient"/>: a header goes on the request. A client is cheap and safe to share across
/// threads. Get one from <see cref="IHttpResultClientFactory"/>, or build one around any <see cref="HttpClient"/>.
/// </remarks>
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "The overloads differ in generic arity and return type (a Result or a Result<T>), so a call never binds to the wrong one; the rule guards against ambiguity if an optional parameter is added to one later.")]
public sealed class HttpResultClient
{
    private readonly HttpClient _client;
    private readonly HttpResultOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>Wraps an <see cref="HttpClient"/>.</summary>
    /// <param name="client">The client. Its <see cref="HttpClient.BaseAddress"/> is what relative paths resolve against.</param>
    /// <param name="options">The settings; the defaults when null.</param>
    /// <param name="logger">Where calls are logged (see <see cref="HttpResultOptions.LogCalls"/>); nothing is logged when null.</param>
    /// <param name="timeProvider">The clock used for a <c>Retry-After</c> date; the system clock when null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public HttpResultClient(
        HttpClient client,
        HttpResultOptions? options = null,
        ILogger<HttpResultClient>? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _options = options ?? new HttpResultOptions();
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;

        var failures = OptionsValidator.Check(_options);

        if (failures.Count > 0)
        {
            throw new ArgumentException("The options are not valid: " + string.Join(" ", failures), nameof(options));
        }

        // Looking up the metadata of a type needs options that are fixed; with no resolver of the caller's own the default one is added.
        if (!_options.Json.IsReadOnly)
        {
            _options.Json.MakeReadOnly(populateMissingResolver: true);
        }
    }

    /// <summary>Sends a request and reports whether it succeeded. The body of a success is not read.</summary>
    /// <param name="request">The request. It is not disposed.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call; this is thrown, not returned.</param>
    /// <returns>A success, or the error.</returns>
    public async Task<Result> SendAsync(
        HttpRequestMessage request,
        HttpCallOptions? call = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var method = request.Method.Method;
        var host = HostOf(request.RequestUri);
        var started = Stopwatch.GetTimestamp();

        using var linked = Link(call, cancellationToken);
        Result result;
        int? status = null;

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            status = (int)response.StatusCode;
            result = await ReadCoreAsync(response, method, host, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryMap(exception, method, host, cancellationToken, out var error))
        {
            result = Result.Failure(error);
        }

        Log(method, host, status, result.Error, started);

        return result;
    }

    /// <summary>
    /// Sends a request and reads the JSON body of a success as <typeparamref name="T"/>. A success without a JSON body is the failure
    /// <c>Http.InvalidResponse</c>.
    /// </summary>
    /// <typeparam name="T">The type of the body.</typeparam>
    /// <param name="request">The request. It is not disposed.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call; this is thrown, not returned.</param>
    /// <returns>The value, or the error.</returns>
    public async Task<Result<T>> SendAsync<T>(
        HttpRequestMessage request,
        HttpCallOptions? call = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var typeInfo = TypeInfo<T>();
        var method = request.Method.Method;
        var host = HostOf(request.RequestUri);
        var started = Stopwatch.GetTimestamp();

        using var linked = Link(call, cancellationToken);
        Result<T> result;
        int? status = null;

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);

            status = (int)response.StatusCode;
            result = await ReadCoreAsync(response, method, host, typeInfo, call, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryMap(exception, method, host, cancellationToken, out var error))
        {
            result = Result.Failure<T>(error);
        }

        Log(method, host, status, result.Error, started);

        return result;
    }

    /// <summary>
    /// Reads a response that was received some other way, with the same rules as a call: a success, or the error for a status that is
    /// not one. The body of a success is not read. The response is not disposed.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="call">Settings of this call, or null; only the timeout applies.</param>
    /// <param name="cancellationToken">Cancels the read; this is thrown, not returned.</param>
    /// <returns>A success, or the error.</returns>
    public async Task<Result> ReadAsync(
        HttpResponseMessage response,
        HttpCallOptions? call = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var method = response.RequestMessage?.Method.Method ?? "unknown";
        var host = HostOf(response.RequestMessage?.RequestUri);
        var started = Stopwatch.GetTimestamp();

        using var linked = Link(call, cancellationToken);
        Result result;

        try
        {
            result = await ReadCoreAsync(response, method, host, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryMap(exception, method, host, cancellationToken, out var error))
        {
            result = Result.Failure(error);
        }

        Log(method, host, (int)response.StatusCode, result.Error, started);

        return result;
    }

    /// <summary>
    /// Reads a response that was received some other way, with the same rules as a call, and reads the JSON body of a success as
    /// <typeparamref name="T"/>. The response is not disposed.
    /// </summary>
    /// <typeparam name="T">The type of the body.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the read; this is thrown, not returned.</param>
    /// <returns>The value, or the error.</returns>
    public async Task<Result<T>> ReadAsync<T>(
        HttpResponseMessage response,
        HttpCallOptions? call = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var typeInfo = TypeInfo<T>();
        var method = response.RequestMessage?.Method.Method ?? "unknown";
        var host = HostOf(response.RequestMessage?.RequestUri);
        var started = Stopwatch.GetTimestamp();

        using var linked = Link(call, cancellationToken);
        Result<T> result;

        try
        {
            result = await ReadCoreAsync(response, method, host, typeInfo, call, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (TryMap(exception, method, host, cancellationToken, out var error))
        {
            result = Result.Failure<T>(error);
        }

        Log(method, host, (int)response.StatusCode, result.Error, started);

        return result;
    }

    /// <summary>Gets <paramref name="path"/> and reads the JSON body as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type of the body.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI. Escape what you put in it.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    public async Task<Result<T>> GetAsync<T>(string path, HttpCallOptions? call = null, CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Get, path);

        return await SendAsync<T>(request, call, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes <paramref name="path"/>.</summary>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A success, or the error.</returns>
    public async Task<Result> DeleteAsync(string path, HttpCallOptions? call = null, CancellationToken cancellationToken = default)
    {
        using var request = Create(HttpMethod.Delete, path);

        return await SendAsync(request, call, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Posts <paramref name="body"/> as JSON and reports whether it succeeded.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A success, or the error.</returns>
    public Task<Result> PostAsync<TBody>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Post, path, body, call, cancellationToken);

    /// <summary>Posts <paramref name="body"/> as JSON and reads the JSON answer as <typeparamref name="TResponse"/>.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <typeparam name="TResponse">The type of the answer.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    public Task<Result<TResponse>> PostAsync<TBody, TResponse>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<TBody, TResponse>(HttpMethod.Post, path, body, call, cancellationToken);

    /// <summary>Puts <paramref name="body"/> as JSON and reports whether it succeeded.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A success, or the error.</returns>
    public Task<Result> PutAsync<TBody>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Put, path, body, call, cancellationToken);

    /// <summary>Puts <paramref name="body"/> as JSON and reads the JSON answer as <typeparamref name="TResponse"/>.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <typeparam name="TResponse">The type of the answer.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    public Task<Result<TResponse>> PutAsync<TBody, TResponse>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<TBody, TResponse>(HttpMethod.Put, path, body, call, cancellationToken);

    /// <summary>Patches <paramref name="body"/> as JSON and reports whether it succeeded.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A success, or the error.</returns>
    public Task<Result> PatchAsync<TBody>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync(HttpMethod.Patch, path, body, call, cancellationToken);

    /// <summary>Patches <paramref name="body"/> as JSON and reads the JSON answer as <typeparamref name="TResponse"/>.</summary>
    /// <typeparam name="TBody">The type of the body.</typeparam>
    /// <typeparam name="TResponse">The type of the answer.</typeparam>
    /// <param name="path">A path relative to the base address, or an absolute URI.</param>
    /// <param name="body">The body.</param>
    /// <param name="call">Settings of this call, or null.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The value, or the error.</returns>
    public Task<Result<TResponse>> PatchAsync<TBody, TResponse>(string path, TBody body, HttpCallOptions? call = null, CancellationToken cancellationToken = default) =>
        SendJsonAsync<TBody, TResponse>(HttpMethod.Patch, path, body, call, cancellationToken);

    private async Task<Result> SendJsonAsync<TBody>(HttpMethod method, string path, TBody body, HttpCallOptions? call, CancellationToken cancellationToken)
    {
        using var request = Create(method, path);
        request.Content = JsonContent.Create(body, TypeInfo<TBody>());

        return await SendAsync(request, call, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<TResponse>> SendJsonAsync<TBody, TResponse>(HttpMethod method, string path, TBody body, HttpCallOptions? call, CancellationToken cancellationToken)
    {
        using var request = Create(method, path);
        request.Content = JsonContent.Create(body, TypeInfo<TBody>());

        return await SendAsync<TResponse>(request, call, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage Create(HttpMethod method, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return new HttpRequestMessage(method, new Uri(path, UriKind.RelativeOrAbsolute));
    }

    // The options hold the metadata once built, so this is a lookup. It throws if the options can read nothing (reflection off and no
    // resolver): that is a mistake in the setup, not a failure of a call.
    private JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)_options.Json.GetTypeInfo(typeof(T));

    private CancellationTokenSource Link(HttpCallOptions? call, CancellationToken cancellationToken)
    {
        var timeout = call?.Timeout ?? _options.Timeout;

        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(call), timeout, "The timeout of a call must be positive or Timeout.InfiniteTimeSpan.");
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        linked.CancelAfter(timeout);

        return linked;
    }

    private async Task<Result> ReadCoreAsync(HttpResponseMessage response, string method, string host, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return Result.Success();
        }

        return Result.Failure(await FailureAsync(response, method, host, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Result<T>> ReadCoreAsync<T>(
        HttpResponseMessage response,
        string method,
        string host,
        JsonTypeInfo<T> typeInfo,
        HttpCallOptions? call,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<T>(await FailureAsync(response, method, host, cancellationToken).ConfigureAwait(false));
        }

        var content = response.Content;
        var length = content.Headers.ContentLength;
        var max = call?.MaxResponseBytes ?? _options.MaxResponseBytes;

        if (response.StatusCode == HttpStatusCode.NoContent || length == 0 || !MediaTypes.IsJson(content.Headers.ContentType?.MediaType))
        {
            return Result.Failure<T>(NoBodyError(ErrorFactory.InvalidResponse, method, host, response));
        }

        if (length > max)
        {
            return Result.Failure<T>(NoBodyError(ErrorFactory.TooLarge, method, host, response));
        }

        try
        {
            // The stream belongs to the response, which whoever made the call disposes.
            var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var value = await JsonSerializer.DeserializeAsync(new BoundedStream(stream, max), typeInfo, cancellationToken).ConfigureAwait(false);

            return value is null
                ? Result.Failure<T>(NoBodyError(ErrorFactory.InvalidResponse, method, host, response))
                : Result.Success(value);
        }
        catch (JsonException)
        {
            return Result.Failure<T>(NoBodyError(ErrorFactory.InvalidResponse, method, host, response));
        }
        catch (ResponseTooLargeException)
        {
            return Result.Failure<T>(NoBodyError(ErrorFactory.TooLarge, method, host, response));
        }
    }

    private static Error NoBodyError(string code, string method, string host, HttpResponseMessage response) =>
        ErrorFactory.NoResponse(code, ErrorFactory.Describe(code), new HttpFailure((int)response.StatusCode, method, host, null, null, null));

    private async Task<Error> FailureAsync(HttpResponseMessage response, string method, string host, CancellationToken cancellationToken)
    {
        var context = new HttpErrorContext(response, method, host, _options, _time);

        try
        {
            foreach (var reader in _options.ErrorReaders)
            {
                if (await reader.ReadAsync(context, cancellationToken).ConfigureAwait(false) is { } error)
                {
                    return error;
                }
            }

            return await ProblemReader.ReadAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Release();
        }
    }

    // Decides which exceptions are failures. The caller's own cancellation never is, and an exception nobody expects is a bug, so
    // it is not caught.
    private bool TryMap(Exception exception, string method, string host, CancellationToken caller, out Error error)
    {
        if (exception is OperationCanceledException && caller.IsCancellationRequested)
        {
            error = default;

            return false;
        }

        if (_options.Exceptions.TryMap(exception, out error))
        {
            return true;
        }

        var failure = new HttpFailure(null, method, host, null, null, null);

        switch (exception)
        {
            case OperationCanceledException or TimeoutException:
                error = ErrorFactory.NoResponse(ErrorFactory.Timeout, ErrorFactory.Describe(ErrorFactory.Timeout), failure);

                return true;
            case HttpRequestException or IOException:
                error = ErrorFactory.NoResponse(ErrorFactory.Unavailable, ErrorFactory.Describe(ErrorFactory.Unavailable), failure);

                return true;
            default:
                error = default;

                return false;
        }
    }

    private string HostOf(Uri? uri)
    {
        var target = uri is { IsAbsoluteUri: true } ? uri : _client.BaseAddress;

        return target is { IsAbsoluteUri: true } ? target.Authority : "unknown";
    }

    private void Log(string method, string host, int? status, Error error, long started)
    {
        if (!_options.LogCalls)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (error.IsNone)
        {
            HttpLog.Succeeded(_logger, method, host, status ?? 0, elapsed);
        }
        else if (status is { } code && code is >= 400 and < 500)
        {
            HttpLog.Refused(_logger, method, host, code, error.Code, elapsed);
        }
        else if (status is { } serverCode)
        {
            HttpLog.FailedWithStatus(_logger, method, host, serverCode, error.Code, elapsed);
        }
        else
        {
            HttpLog.FailedWithoutStatus(_logger, method, host, error.Code, elapsed);
        }
    }
}
