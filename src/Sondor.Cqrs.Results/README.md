# Sondor.Cqrs.Results

[`Sondor.Results`](../Sondor.Results) support for [`Sondor.Cqrs`](../Sondor.Cqrs). With it, a request that returns a
`Result` or `Result<T>` is reported properly: a failed result is a failure in the traces, metrics and logs, with its error
code, and not a success that happened to return normally.

```bash
dotnet add package Sondor.Cqrs.Results
```

```csharp
services.AddCqrsResults();           // before or after AddCqrs
services.AddCqrs(cqrs =>
{
    cqrs.AddHandlersFromAssemblyContaining<GetNodeHandler>();
    cqrs.UseTracing();
    cqrs.UseMetrics();
    cqrs.UseLogging();
});
```

A handler returns a `Result<T>` as usual:

```csharp
public sealed record GetNode(Guid Id) : IQuery<Result<Node>>;

public sealed class GetNodeHandler(INodeStore store) : IRequestHandler<GetNode, Result<Node>>
{
    public async ValueTask<Result<Node>> Handle(GetNode request, CancellationToken ct) =>
        await store.FindAsync(request.Id, ct) is { } node ? node : NodeErrors.NotFound;
}
```

## What changes

| Built in | With `AddCqrsResults()` |
|---|---|
| Tracing | A failed result sets the span status to error, with tags `cqrs.outcome` = `failure` and `cqrs.error.code` = the error code. |
| Metrics | A failed result is counted with outcome `failure`, not `success`. |
| Logging | A failed result is logged as "Refused ... with *code*". |

Only an error code is ever recorded, never the error description or any value.

## Refusing a request with a result

A behaviour that must stop a request (validation, authorization) builds the failed response through an
`IFailureFactory<TResponse>`. This package registers one for `Result` and `Result<T>`:

```csharp
public sealed class RequireTenant<TRequest, TResponse>(IEnumerable<IFailureFactory<TResponse>> factories)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken ct) =>
        factories.FirstOrDefault() is { } factory && !HasTenant(request)
            ? ValueTask.FromResult(factory.Create(new RequestFailure("Tenant.Missing", "A tenant is required.", FailureKind.Failure)))
            : continuation.Invoke();
}
```

**Ask for `IEnumerable<IFailureFactory<TResponse>>`, not `IFailureFactory<TResponse>`.** The factory only exists for result
types, and the container throws, rather than returning nothing, when a single service is asked for a response type that
cannot have one. A collection comes back empty, and the behaviour can fall back (to throwing, for example).

## Things that behave in a specific way

- A response that is not a result (a `string`, a record, `Unit`) is read as a success, exactly as without this package, so it
  is safe to use with requests of every kind.
- A reader or factory you register yourself for a response type is used instead of the one in this package.
- A `Validation` refusal becomes an `Error` of type `Validation`; every other refusal becomes type `Failure`.
- Other result types that implement `IResultState` are read, but cannot be built by the factory. Register an
  `IFailureFactory<T>` of your own for those.
