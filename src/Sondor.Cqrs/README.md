# Sondor.Cqrs

Commands, queries and notifications dispatched in process, with a pipeline of behaviours around them. MIT licensed, no
licence key, and no third-party dependencies: the only packages it uses are Microsoft's dependency injection and logging
abstractions.

```bash
dotnet add package Sondor.Cqrs
```

## The basics

```csharp
using Sondor.Cqrs;

// A query: it only reads. A command (ICommand<T>) changes something. IRequest<T> is neither.
public sealed record GetNode(Guid Id) : IQuery<Node?>;

public sealed class GetNodeHandler(INodeStore store) : IRequestHandler<GetNode, Node?>
{
    public ValueTask<Node?> Handle(GetNode request, CancellationToken cancellationToken) =>
        store.FindAsync(request.Id, cancellationToken);
}
```

Register it once, then send requests through `ISender`:

```csharp
services.AddCqrs(cqrs => cqrs.AddHandlersFromAssemblyContaining<GetNodeHandler>());

public sealed class NodeEndpoint(ISender sender)
{
    public ValueTask<Node?> Get(Guid id, CancellationToken ct) => sender.Send(new GetNode(id), ct);
}
```

A request with nothing to return implements `ICommand` (or `IRequest`) and its handler returns `Unit.Value`.

Any response type works. Returning `Result<T>` from [`Sondor.Results`](../Sondor.Results) is recommended, not required.

## Notifications

A notification can have any number of handlers, including none.

```csharp
public sealed record NodeProvisioned(Guid Id) : INotification;

public sealed class SendWelcome : INotificationHandler<NodeProvisioned>
{
    public ValueTask Handle(NodeProvisioned notification, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

await publisher.Publish(new NodeProvisioned(id), ct);     // IPublisher
```

Handlers run one after another and the first that throws stops the rest. For all at once, use
`cqrs.UseNotificationPublisher<ParallelNotificationPublisher>()`, or write an `INotificationPublisher`.

## The pipeline

A behaviour runs around every request it applies to. The first one added is the outermost.

```csharp
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken ct)
    {
        try { return await continuation(); }
        catch (TimeoutException) { return await continuation(); }     // may be called more than once, or not at all
    }
}

services.AddCqrs(cqrs =>
{
    cqrs.AddHandlersFromAssemblyContaining<GetNodeHandler>();
    cqrs.UseTracing();                       // built in, opt-in
    cqrs.UseMetrics();
    cqrs.UseLogging(slowRequestThreshold: TimeSpan.FromSeconds(1));
    cqrs.AddBehavior(typeof(RetryBehavior<,>));
});
```

| Built in | What it records |
|---|---|
| `UseTracing()` | A span for each request, and for each notification and its handlers. Source `Sondor.Cqrs`; tags `cqrs.request`, `cqrs.request.kind` (command, query, request, notification), `cqrs.handler`, `cqrs.outcome`, `cqrs.error.code`. Exceptions are recorded on the span. |
| `UseMetrics()` | Meter `Sondor.Cqrs`: a counter `cqrs.requests` and a histogram `cqrs.request.duration` (seconds), tagged by request, kind and outcome (`success`, `failure`, `error`, `cancelled`). |
| `UseLogging()` | One line per request: its kind and name, how long it took, how it ended. **Never the request itself**, which can hold a secret. A slow request is a warning. |

Tracing, metrics and logging do nothing while nothing is listening. Add the names to your OpenTelemetry setup:
`.AddSource(CqrsTelemetry.SourceName)` and `.AddMeter(CqrsTelemetry.MeterName)`.

### Failures that are returned, not thrown

When requests return a result type, a "failed" response is still a response. Register an `IOutcomeReader<TResponse>` and the
built-ins report it as a failure, with its code:

```csharp
services.AddSingleton<IOutcomeReader<MyResult>, MyResultOutcomeReader>();
```

Without one, every response that comes back counts as a success. An `IFailureFactory<TResponse>` lets a behaviour refuse a
request by returning a failed response. Adapter packages register both for you.

## Registration checks

`AddCqrs` fails at start-up, with the type names, when two handlers handle one request, or when a request type in a scanned
assembly has no handler (turn the second off with `cqrs.ValidateHandlers = false` if a handler is registered elsewhere).
Sending a request that has no handler throws an `InvalidOperationException` that says so.

Handlers are transient unless you set `cqrs.HandlerLifetime`. Handlers that are generic classes are not found by the scan;
register those yourself.

## Things that behave in a specific way

- `Send` and `Publish` return `ValueTask`. Await each one once.
- `Publish` uses the **runtime type** of the notification, so publishing through an `INotification` variable reaches the
  handlers of the type it really is.
- `ISender` and `IPublisher` are transient and use the scope they are resolved from, so scoped handlers and behaviours work.
- Nothing is on unless asked for, and nothing scans, reflects or builds a second service provider per request.
