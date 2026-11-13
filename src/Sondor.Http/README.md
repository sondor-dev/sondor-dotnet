# Sondor.Http

Read the answer of another HTTP service **as a value**. A call returns a [`Sondor.Results`](../Sondor.Results) `Result` or
`Result<T>`: a status that is not a success, a problem document, a timeout and a host that cannot be reached are all failed
results with a **stable error code**, never exceptions. It is the client-side counterpart of
[`Sondor.AspNetCore`](../Sondor.AspNetCore) and reads exactly the response format that package promises.

It builds on `HttpClient` and `IHttpClientFactory` and adds no connection handling, no retries and no tracing of its own.

```bash
dotnet add package Sondor.Http
```

```csharp
builder.Services.AddSondorHttpClient("orders", options =>
{
    options.BaseAddress = new Uri("https://orders.example.com/");
    options.Timeout = TimeSpan.FromSeconds(10);
});

public sealed class OrdersClient(IHttpResultClientFactory clients)
{
    private readonly HttpResultClient _http = clients.CreateClient("orders");

    public Task<Result<Order>> Find(Guid id, CancellationToken cancellationToken) =>
        _http.GetAsync<Order>($"orders/{id}", cancellationToken: cancellationToken);
}
```

```csharp
var result = await orders.Find(id, cancellationToken);

if (result.IsFailure)
{
    // result.Error.Code is the other side's own stable code ("Order.NotFound"), or one made from the status ("Request.NotFound")
    // result.Error.Type is NotFound
    return result.Error;
}

var order = result.Value;
```

## Why use it

Every service that calls another repeats the same code: turn a status into an error instead of throwing, tell the caller's
cancellation from a timeout, read the other side's error body safely, survive a response that is not what was promised, send a
credential only where it belongs, log a call without recording a secret. The framework supplies the transport
(`HttpClient`), the handler pipeline (`IHttpClientFactory`), resilience (`Microsoft.Extensions.Http.Resilience`), spans and
metrics (`System.Net.Http`) and JSON (`System.Net.Http.Json`). What it does not supply is **response to `Result`**, with one error
model. That is this package.

- **No exceptions for expected failures.** A 404, a 409, a timeout and an unreachable host are values you handle with `Match`,
  `Bind` and the rest of `Sondor.Results`.
- **The other side's error, read safely.** The RFC 9457 problem document is read into an `Error` with its code, and its trace id,
  `Retry-After` and per-property errors stay available. Sizes are bounded, and the other side's words are never trusted.
- **Bounded.** A body larger than allowed is not read. A response that is not JSON, or is empty, is a failure you can name, not a
  parse exception.
- **A call timeout that is a value.** `Http.Timeout` is a failure; the caller's own cancellation still throws, as it should.
- **Nothing shared is changed.** A header, a token or a language goes on the request, never on the client's defaults, so
  concurrent callers cannot see each other's.
- **Opt-in pieces.** A token handler that only sends the token to hosts you allow, and a hook for other APIs' error formats.

If you only call one or two APIs, want exceptions, or use generated clients (Kiota, NSwag) that bring their own error types, you
do not need this package. [Refit](https://github.com/reactiveui/refit) is the better tool for interface-declared clients; its
responses can still be read with `HttpResultClient.ReadAsync`.

## Making calls

```csharp
var order  = await http.GetAsync<Order>("orders/42");                         // Result<Order>
var made   = await http.PostAsync<NewOrder, Order>("orders", newOrder);       // body as JSON, answer as JSON
var saved  = await http.PutAsync("orders/42", order);                         // Result: only whether it worked
var gone   = await http.DeleteAsync("orders/42");                             // Result
```

Paths are relative to the base address (an absolute URI is sent as it is). `PatchAsync` works like `PutAsync`. Anything else goes
through `SendAsync` with your own `HttpRequestMessage`, which is not disposed:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "orders?status=open");
request.Headers.Add("X-Correlation-Id", correlationId);               // per request, never on the shared client

Result<List<Order>> open = await http.SendAsync<List<Order>>(request);
```

A call returning `Result` does not read the body of a success. A call returning `Result<T>` needs a JSON content type and a body;
an empty or non-JSON success is the failure `Http.InvalidResponse`, because a `Result<T>` always has a value. A **business
failure inside a 200** (an envelope with `success: false`) is for you to decide, with `Result<T>.Ensure`.

### Per call

```csharp
var result = await http.GetAsync<Report>("reports/42", new HttpCallOptions
{
    Timeout = TimeSpan.FromMinutes(2),        // instead of the client's
    MaxResponseBytes = 100 * 1024 * 1024      // instead of the client's
});
```

### Reading a response you got some other way

`ReadAsync` applies the same rules to any `HttpResponseMessage`: it does not dispose it.

```csharp
using var response = await someOtherClient.SendAsync(request);

Result<Order> result = await http.ReadAsync<Order>(response);
```

## What a failure looks like

| Situation | Code | Type |
|---|---|---|
| The other side sent a problem with a valid `errorCode` | that code | from the status |
| No valid code, or no problem document | from the status: `Request.Invalid` 400 and 422, `Request.Unauthorized` 401, `Request.Forbidden` 403, `Request.NotFound` 404, `Request.Conflict` 409, `Request.UnsupportedMediaType` 415, `Request.RateLimited` 429, `Feature.Disabled` 501, `Request.Unavailable` 503, `Request.GatewayTimeout` 504; any other status `Request.Failed`, or `Error.Internal` from 500 | from the status |
| The host could not be reached, the connection dropped | `Http.Unavailable` | `Failure` |
| The call took longer than allowed | `Http.Timeout` | `Failure` |
| The body is larger than allowed | `Http.TooLarge` | `Failure` |
| A success that is empty, not JSON, or does not parse | `Http.InvalidResponse` | `Failure` |

The fallback codes are the ones `Sondor.AspNetCore` uses for the problems it makes on its own, so a service has one vocabulary.
The type of an error is the inverse of that package's status table: 400 and 422 `Validation`, 401 `Unauthorized`, 403
`Forbidden`, 404 `NotFound`, 409 `Conflict`, 429 `RateLimited`, 501 `FeatureDisabled`, 500 `InternalServerError`, 502 to 504
`Failure`; any other 4xx is `Failure` and any other 5xx `InternalServerError`. All of it can be changed (see
[Settings](#settings)).

**The caller's own cancellation is not a value.** Cancel the token you passed and an `OperationCanceledException` is thrown, as
everywhere in .NET. Only a timeout of the call (or of the `HttpClient`) is the failure `Http.Timeout`. An exception that is
neither (a bug in your code) is thrown as usual; map the ones you expect with [`Exceptions`](#settings).

### The context of a failure

An error carries more than a code. `GetHttpFailure()` returns what is known about the call:

```csharp
if (result.Error.GetHttpFailure() is { } failure)
{
    failure.Status;                  // 404, or null when there was no response (unreachable, timed out)
    failure.Method;  failure.Host;   // "GET", "orders.example.com"; never the path or the query
    failure.TraceId;                 // the other side's trace id, to quote when reporting a problem
    failure.RetryAfter;              // TimeSpan?, from a Retry-After header (seconds or a date), for a 429 or 503
    failure.Problem?.Errors;         // property name to messages, when the other side sent them
    failure.Problem?.Detail;         // the other side's words: untrusted
}
```

It is kept in `Error.Arguments` as one immutable object, so a success carries nothing and `ToString()` on an error, a failure or a
problem shows no value, only its kind.

### What is trusted

The other side's text is **data, not yours**. `Error.Description` is defined by `Sondor.Results` as safe to show, so the other
side's `detail` is not copied into it: the description is a generic English sentence for the status, and the other side's words
stay in `Problem.Detail`. Set `UseRemoteDetail` to pass them through, trimmed, with control characters removed and cut to
`MaxRemoteDetailLength`. The other side's `errorCode` is accepted only when it is 1 to 128 letters, digits, dots, underscores or
hyphens; anything else falls back to the status, so an unbounded value cannot reach a metric or a log through it. Problem
documents are read with limits (100 properties, 20 messages each, bounded text, nesting of at most 32 levels).

## Settings

Every setting has a default, so `AddSondorHttpClient("orders")` alone is a valid setup. They are validated when the host starts.

```csharp
builder.Services.AddSondorHttpClient("orders", options =>
{
    options.BaseAddress = new Uri("https://orders.example.com/");
    options.CodeByStatus[404] = "Order.Missing";           // change the fallback code of a status
    options.TypeByStatus[404] = ErrorType.Failure;         // or its type
});
```

The plain values bind from configuration, with a section name of your choice:
`services.Configure<HttpResultOptions>("orders", configuration.GetSection("Http:Orders"))`.

| Setting | Default | Meaning |
|---|---|---|
| `BaseAddress` | none | What relative paths resolve against. Applied to the `HttpClient` of a registered client. |
| `Timeout` | 30 seconds | The longest a call may take, from sending to having read the body. `Timeout.InfiniteTimeSpan` turns it off. Also set as the `HttpClient.Timeout`, so a client taken straight from the factory has the same limit. |
| `MaxResponseBytes` | 10 MiB | The largest body of a success, after decompression. |
| `MaxErrorBodyBytes` | 64 KiB | The largest error body that is read at all; a larger one is cut and not parsed. |
| `Json` | web defaults | How bodies are read and written. Set a `TypeInfoResolver` from a source-generated context for trimming and Native AOT. It becomes read-only on first use. |
| `ErrorCodeMember` | `errorCode` | The member of a problem that holds the code. |
| `CodeByStatus`, `TypeByStatus` | see above | The fallback code and the type of an error for each status. |
| `UseRemoteDetail` | `false` | Use the other side's `detail` as the description. |
| `MaxRemoteDetailLength` | 256 | The longest remote `detail` used. |
| `LogCalls` | `true` | Log each call (see [Logging](#logging)). |
| `ErrorReaders` | none | Readers for other error formats (see [Other error formats](#other-error-formats)). |
| `Exceptions` | the package's own | Exceptions reported as an error instead of thrown. |

`HttpClient` is registered through `IHttpClientFactory`, so everything you know works: `AddSondorHttpClient` returns the framework's
`IHttpClientBuilder`. Register a class that uses the client in one step:
`services.AddSondorHttpClient<OrdersClient>("orders", ...)`; its constructor takes the `HttpResultClient` next to anything else.

## Other error formats

An API that does not speak RFC 9457 (an envelope with `success: false`, a nested `error.code`) is read by a small reader of your
own. It sees the status, the headers and a bounded body that is read once and shared, and returns an `Error` or `null` to let the
next reader, then the problem reader, then the status have a go:

```csharp
internal sealed class GatewayErrorReader : IHttpErrorReader
{
    public async ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken)
    {
        var document = await context.ReadJsonAsync(cancellationToken);    // null when it is not JSON, cut, or broken

        if (document?.RootElement.TryGetProperty("error", out var error) != true)
        {
            return null;                                                   // not this format
        }

        // A fixed set of codes, not the other side's text. CreateError keeps the status, Retry-After and the host on the error.
        return context.CreateError("Gateway.Rejected", "The gateway refused the request.", ErrorType.Validation);
    }
}

builder.Services.AddSondorHttpClient("gateway", ...).AddErrorReader<GatewayErrorReader>();
```

A reader is shared by every call of its client, so it must be safe to use from several threads. What it puts in the error is its
own responsibility: the body is the other side's text.

## Sending a token

```csharp
builder.Services.AddSondorHttpClient("orders", options => options.BaseAddress = new Uri("https://orders.example.com/"))
    .AddSondorBearerToken<MyTokenProvider>();           // or a delegate: (request, cancellationToken) => token

public interface IAccessTokenProvider
{
    ValueTask<string?> GetTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
```

The handler sets `Authorization: Bearer` **on the request**, never on the client's defaults, and only when:

- the request goes to an allowed host: the host of the `BaseAddress`, or the hosts in `BearerTokenOptions.AllowedHosts`. A link
  from a paging response, or any absolute URI that points elsewhere, is sent **without** the token (and a warning with the host,
  never the token, is logged);
- the scheme is `https` (`AllowInsecureHttp` for local development);
- the request has no `Authorization` header of its own.

With neither a `BaseAddress` nor an allowed host the host does not start. The provider caches and refreshes the token; the package
does not, and never logs it.

## Resilience

Retries, circuit breaking and hedging are the job of `Microsoft.Extensions.Http.Resilience` (`using Microsoft.Extensions.Http.Resilience;`), and the package does not redo them:

```csharp
builder.Services.AddSondorHttpClient("orders", ...)
    .AddStandardResilienceHandler(options => options.Retry.DisableForUnsafeHttpMethods());   // do not retry a POST
```

The resilience handlers report their own conditions as exceptions (`using Polly.Timeout; using Polly.CircuitBreaker;`). Map the ones you want as values:

```csharp
options.Exceptions
    .Map<TimeoutRejectedException>(_ => new Error("Http.Timeout", "The call took longer than allowed.", ErrorType.Failure))
    .Map<BrokenCircuitException>(_ => new Error("Http.Unavailable", "The service is not available.", ErrorType.Failure));
```

## Logging

A call logs one line: **debug** for a success, **information** for a 4xx, **warning** for anything else (a 5xx, a timeout, an
unreachable host, a success that cannot be used). It holds the method, the host, the status, the elapsed time and the error code,
and **never** a path, a query string, a header or a body. `LogCalls = false` switches it off.

`IHttpClientFactory` logs on its own too, and that is the framework's: its lines hold the full path of a request (the values of
the query string are replaced by `*`, but a path that holds an identifier or a secret is logged). If a path can be sensitive,
remove the framework's loggers on that client with `.RemoveAllLoggers()` and rely on the line above.

Spans, the `traceparent` header and metrics come from `System.Net.Http`; the package adds none of its own.

## Good to know

- **Not thread-bound.** `HttpResultClient` and its factory are safe to use from several threads, and a client is cheap to create.
- **Dispose requests you create.** `SendAsync` does not dispose the request or the response of `ReadAsync`; the convenience methods
  (`GetAsync` and the others) dispose their own.
- **A body is read once.** Do not read the content of a response you pass to `ReadAsync` beforehand.
- Target framework: .NET 10.
