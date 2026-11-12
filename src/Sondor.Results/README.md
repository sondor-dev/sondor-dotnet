# Sondor.Results

Failures as values. An operation that can fail in an expected way returns a `Result` instead of throwing, so the failure is
part of the method's signature and the caller has to deal with it. No dependencies.

```bash
dotnet add package Sondor.Results
```

## The basics

```csharp
using Sondor.Results;

public sealed class NodeService
{
    private static readonly Error NotFound =
        new("Node.NotFound", "There is no node with that id.", ErrorType.NotFound);

    // Returns a value or an error. A value and an Error both convert implicitly, so there is nothing to wrap.
    public Result<Node> Find(Guid id) =>
        _nodes.TryGetValue(id, out var node) ? node : NotFound;

    // Returns nothing, or an error.
    public Result Remove(Guid id) =>
        _nodes.Remove(id) ? Result.Success() : NotFound;
}
```

The caller decides what a failure means:

```csharp
var result = service.Find(id);

if (result.IsFailure)
{
    return Problem(result.Error);          // Code: "Node.NotFound", Type: NotFound
}

var node = result.Value;                   // only valid on a success
```

## Errors

An `Error` has three parts:

| Part | What it is |
|---|---|
| `Code` | A stable code in the form `Area.Reason`, such as `Node.NotFound`. Callers and clients depend on it, so it never changes once released, and it never contains data from the request. |
| `Description` | A message for a person, safe to show to the caller. |
| `Type` | The kind of failure (`Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `RateLimited`, `FeatureDisabled`, `InternalServerError`, `Failure`). It says how to treat the failure, not how it is transported. |

Define your errors once, as `static readonly` fields or small factory methods, so a code is spelled in one place.

## Chaining

```csharp
Result<Order> order = FindCustomer(id)
    .Ensure(customer => customer.IsActive, Errors.CustomerInactive)   // a success that fails a rule becomes a failure
    .Bind(customer => CreateOrder(customer))                          // the next step can fail too
    .Map(order => order with { Reference = NextReference() })         // change the value
    .Tap(order => log.Created(order.Id));                             // a side effect, on success only

string message = order.Match(
    value => $"Created {value.Reference}",
    error => $"Could not create the order: {error.Code}");
```

A failure skips every step after it and comes out at the end unchanged.

| Method | Does |
|---|---|
| `Map` | Turns the value of a success into another value |
| `Bind` | Runs the next step, which returns a `Result` |
| `Tap` | Runs a side effect on success, and returns the same result |
| `Ensure` | Turns a success into a given failure when the value does not satisfy a rule |
| `Match` | Returns one thing for a success and another for a failure |
| `TryGetValue` | `out` the value of a success, for code that prefers the `Try` shape |

## Things that behave in a specific way

- **A success always has a value.** `Result.Success<T>(null)` throws; use the non-generic `Result.Success()` for "nothing
  to return". An implicit conversion of a null (`Result<string> r = (string)null!;`) and a `Map` that returns null give the
  failure `Error.NullValue` instead of throwing.
- **`default` is a failure, not a success.** An uninitialised `Result` or `Result<T>` has the error `Error.Unspecified`, so it
  cannot pass for a good one.
- **A failure needs a reason.** `Result.Failure(Error.None)` throws: `Error.None` is the error of a success.
- **Reading the value of a failure throws** `InvalidOperationException`, naming the failure's code. Check `IsSuccess`, or use
  `TryGetValue` or `Match`.
- **`ToString()` shows only the code**, never the value or the description, so logging a result cannot leak either.
- **`Result` and `Result<T>` are structs**, so passing them around does not allocate.

## Reading a result without knowing its type

`IResultState` exposes `IsSuccess` and `Error` for both result types, so cross-cutting code (logging, tracing, a pipeline
behaviour) can look at any outcome:

```csharp
if (response is IResultState { IsSuccess: false } failed)
{
    logger.LogWarning("Request refused: {Code}", failed.Error.Code);
}
```

## Not included

Async helpers (`MapAsync`, `BindAsync`), collecting several errors into one result, and exception-to-result conversion are
not part of this package yet. They can be added without breaking anything; open an issue if you need one.
