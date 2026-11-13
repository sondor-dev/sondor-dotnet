# Sondor.AspNetCore

One consistent [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem response for an ASP.NET Core API. A failed
[`Sondor.Results`](../Sondor.Results) `Result`, an unhandled exception and the framework's own bare `401` or `404` all leave the
service with the same shape, a **stable error code** and a trace id.

It builds on what the framework already provides (`ProblemDetails`, `IExceptionHandler`, status code pages) and adds no middleware
of its own. It works in minimal APIs and in MVC.

```bash
dotnet add package Sondor.AspNetCore
```

```csharp
builder.Services.AddSondorProblemDetails();

var app = builder.Build();

app.UseSondorProblemDetails();       // first: exception handler + problem bodies for bare 401/404/...

app.MapGet("/orders/{id}", (Guid id, IOrders orders) => orders.Find(id).ToHttpResult());
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "No Order was found for the given Id.",
  "instance": "/orders/6f0e...",
  "errorCode": "Resource.NotFound",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

## What it does

- **`Result` to response.** `result.ToHttpResult()` is `204` or `200` with the value on success, and a problem response on
  failure. The status comes from the error's `ErrorType`; the `errorCode` member is its stable `Code`.
- **Exceptions.** An unhandled exception in an API request becomes a generic `500`. Its message and stack trace are logged and
  **never sent**. Map the exceptions you expect to an `Error` and they are reported like any other.
- **The framework's own errors.** A bare `404`, a `401`, an unsupported media type or an invalid body get the same shape and an
  `errorCode`, so a client handles one format.
- **One code per error, never translated.** Clients branch on `errorCode`. Only `title` and `detail` are for people (see
  [Translated text](#translated-text)).

If you do not return `Result` values and are happy with the framework's `AddProblemDetails()` and an `IExceptionHandler` of your
own, you do not need this package. It is for services where failures are values and every error should look the same.

## Using a result

```csharp
// minimal API
app.MapGet("/orders/{id}", (Guid id, IOrders orders) => orders.Find(id).ToHttpResult());

// something other than 200: you decide what a success looks like
app.MapPost("/orders", (NewOrder order, IOrders orders) =>
    orders.Create(order).ToHttpResult(created => TypedResults.Created($"/orders/{created.Id}", created)));

// MVC: an action may return IResult
[ApiController, Route("orders")]
public sealed class OrdersController(IOrders orders) : ControllerBase
{
    [HttpGet("{id}")]
    public IResult Get(Guid id) => orders.Find(id).ToHttpResult();
}
```

`ToHttpResult()` always writes a problem on failure, whatever the caller's `Accept` header says.

## Settings

Every setting has a default, so `AddSondorProblemDetails()` alone is a valid setup. They are validated when the host starts.

```csharp
builder.Services.AddSondorProblemDetails(options =>
{
    options.TypeUriTemplate = "https://docs.example.com/errors/{code}";   // link each code to its documentation
    options.StatusByType[ErrorType.Validation] = 422;                     // change a status
    options.StatusByCode["Order.Late"] = 410;                             // or one code's status
});
```

They can also be bound from configuration: `services.Configure<ErrorResponseOptions>(configuration.GetSection("Errors"))`.

| Setting | Default | Meaning |
|---|---|---|
| `StatusByType` | validation 400, unauthorized 401, forbidden 403, not found 404, conflict 409, rate limited 429, feature disabled 501, internal and failure 500 | Status for each `ErrorType`. Statuses must be 400 to 599. |
| `StatusByCode` | none | The status of one code; wins over `StatusByType`. |
| `TypeUriTemplate` | none | The `type` of a problem, with `{code}` replaced (URL-escaped). Without it the framework's link to the HTTP status is kept. |
| `ErrorCodeMember` | `errorCode` | The name of the code member. |
| `CodeByStatus` | 400 `Request.Invalid`, 401 `Request.Unauthorized`, 403 `Request.Forbidden`, 404 `Request.NotFound`, 409 `Request.Conflict`, 415 `Request.UnsupportedMediaType`, 429 `Request.RateLimited`, 499 `Request.Cancelled` | The code of a problem the framework made on its own. Other statuses get `Request.Failed`, or `Error.Internal` from 500. |
| `HandleExceptions` | `true` | Answer an unhandled exception with a problem. |
| `WriteStatusCodeBodies` | `true` | Give a bare error status a problem body. |
| `IncludeExceptionDetails` | `false` | Put an unexpected exception's message in `detail`. For development only. |
| `BareTraceId` | `false` | Use the trace id alone as `traceId` (handy to paste into a trace viewer) instead of the framework's full `traceparent`. |
| `TagActivity` | `true` | Tag the request's `Activity` with `error.type`, and mark it failed from 500 up. |
| `ClientClosedRequestStatus` | `499` | The status when the caller disconnected before the answer. Never logged as an error. |
| `AppliesTo` | API requests only | Which requests get a problem response (see [Pages and views](#pages-and-views)). |

## Exceptions

An exception that is not mapped is a generic `500` (`Error.Internal`); the exception is logged with its trace id and the caller
sees nothing about it. Map the ones you expect:

```csharp
builder.Services.AddSondorProblemDetails(options =>
{
    options.Exceptions.Map<PaymentDeclinedException>(exception =>
        new Error("Payment.Declined", exception.Message, ErrorType.Conflict));
});
```

The error's description becomes `detail`, so it must be safe to show. A mapping applies to derived types too; the nearest type
wins. A single `AggregateException` or `TargetInvocationException` is unwrapped first. A request whose caller disconnected is
answered with `ClientClosedRequestStatus` and logged at debug level.

## Pages and views

An app that serves Razor Pages or views next to an API keeps its own error pages. The exception handler and the status code
bodies only answer requests `AppliesTo` accepts. By default that is:

- an `[ApiController]` action or a minimal API: yes;
- a Razor Page or a controller that is not an API controller: no;
- a request with no endpoint yet (an unknown URL): yes, unless its `Accept` header prefers HTML.

For a request that is declined, register your own handler. `UseExceptionHandler("/Error")` works next to this package, because
exception handlers registered here are asked first. (Do not call `UseSondorProblemDetails()` in that case: it adds the
framework's parameterless `UseExceptionHandler()`, which answers a declined request itself.)

## Extending it

All three are optional, and the package registers none of them.

```csharp
// The words of a problem (title, detail), for example in the reader's language.
public interface IProblemTextProvider
{
    ProblemText? GetText(Error failure, HttpContext context);       // null: keep the error's English text
}

// More members on a problem, such as per-property validation errors.
public interface IProblemContributor
{
    void Contribute(ProblemContext context);
}

// How an Error becomes a problem at all.
public interface IErrorProblemFactory
{
    ProblemDetails Create(Error failure, HttpContext context, Exception? exception = null);
}
```

Register your own with `services.AddSingleton<IProblemTextProvider, ...>()`; register several contributors and each runs, in
order. The error `Code` is never passed through the text provider: it is the contract with your clients.

### Translated text

Translated `title` and `detail` come from an `IProblemTextProvider`. [`Sondor.Results.Localization`](../Sondor.Results.Localization)
turns an `Error` into text in the current culture from your own resources, and ASP.NET Core's request localization sets that
culture from `Accept-Language`. An adapter package that connects the two is planned; until then it is a few lines:

```csharp
internal sealed class LocalizedProblemText(IErrorLocalizer localizer) : IProblemTextProvider
{
    public ProblemText? GetText(Error failure, HttpContext context)
    {
        var text = localizer.Localize(failure);

        return new ProblemText(text.Title, text.Detail);
    }
}
```

## The response format

These members are a promise. Changing a member's name or meaning is a breaking change, so a client can rely on them.

| Member | Meaning |
|---|---|
| `type`, `title`, `status`, `detail`, `instance` | As in RFC 9457. `instance` is the request path. |
| `errorCode` | The stable code of the error, `Area.Reason`. The name can be changed in the settings. Never translated. |
| `traceId` | The trace of the request, to quote when reporting a problem. |
| `errors` | Added by the framework to an invalid body: property name to messages. |

The content type is `application/problem+json`.

## Good to know

- Call `UseSondorProblemDetails()` early, before routing and authentication, so the exception handler wraps everything after it.
  An app that wants to place the framework's `UseExceptionHandler()` and `UseStatusCodePages()` itself can skip it and call them.
- `AddSondorProblemDetails()` calls the framework's `AddProblemDetails()`. Your own `CustomizeProblemDetails` still runs, after
  this package has set the code, so it can adjust it.
- An exception thrown after the response has started cannot be answered; the framework handles that case as it always did.
- Target framework: .NET 10.
