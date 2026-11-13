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

An error can also carry named **arguments** (the entity, the property, the action behind its message), which is what makes it
translatable: see [Common errors and templates](#common-errors-and-templates).

Define your errors once, as `static readonly` fields, small factory methods or templates, so a code is spelled in one place.

## Common errors and templates

The usual failures are one call each. The description is a plain English sentence built from named arguments, and the
arguments stay on the error so a layer that translates can fill its own wording instead of parsing yours.

```csharp
Error notFound = Error.NotFound("Order", "Id");
// Code:        Resource.NotFound          Type: NotFound
// Description: No Order was found for the given Id.
// Arguments:   Entity = Order, Property = Id

return notFound;                            // converts to a failed Result or Result<T>, like any Error
```

| Factory | Code | Type |
|---|---|---|
| `Error.NotFound(entity, property)` | `Resource.NotFound` | `NotFound` |
| `Error.Conflict(entity, property)` | `Resource.Conflict` | `Conflict` |
| `Error.Invalid(entity, property)` | `Resource.Invalid` | `Validation` |
| `Error.Unauthorized(action)` | `Request.Unauthorized` | `Unauthorized` |
| `Error.Forbidden(action)` | `Request.Forbidden` | `Forbidden` |
| `Error.RateLimited(action)` | `Request.RateLimited` | `RateLimited` |
| `Error.FeatureDisabled(feature)` | `Feature.Disabled` | `FeatureDisabled` |
| `Error.Internal(action)` | `Error.Internal` | `InternalServerError` |
| `Error.Failed(action)` | `Error.Failed` | `Failure` |

The built-in codes are generic: the entity is an argument, not part of the code. When clients should be able to tell one
failure from another, give it a code of your own. It keeps the same words, and the error still names the general template
it came from, so a translation layer with no wording for your code can fall back to the general one:

```csharp
static readonly ErrorTemplate OrderNotFound = ErrorTemplates.NotFound.WithCode("Order.NotFound");

Error error = OrderNotFound.Create(
    ErrorArgument.Term("Entity", "Order"),
    ErrorArgument.Term("Property", "Id"));
// Code: Order.NotFound    Arguments.Template: Resource.NotFound
```

Or declare a template with your own words. Placeholders are plain names in braces; there are no positions, formats or
alignments, so nothing depends on the culture:

```csharp
static readonly ErrorTemplate Overdrawn = new(
    "Account.Overdrawn", ErrorType.Conflict, "The {Account} cannot go below the {Limit}.");

Error error = Overdrawn.Create(
    ErrorArgument.Term("Account", "current account"),
    ErrorArgument.Term("Limit", "overdraft limit"));
```

A template is immutable and safe to share. Declare it as a `static readonly` field; a mistake in its words (an unclosed
brace, `{0}`, a repeated placeholder) throws when it is declared, and a missing or unknown argument throws when the error is
created, so the first test that touches the code finds it.

### What may be shown

Every argument is one of three kinds, and the kind decides whether it can ever reach a person:

| Kind | Made with | Meaning |
|---|---|---|
| `Term` | `ErrorArgument.Term(name, text)` | A name you chose: an entity, a property, an action. Safe to show, and what a translation fills in. |
| `Data` | `ErrorArgument.Data(name, value)` | A value that may have come from a caller (an id, an email, a search text). It travels with the error, but is **not** put in the description, and a layer showing errors should not show it by default. |
| `Sensitive` | `ErrorArgument.Sensitive(name)` | Personal data or a secret. Only the **name** is kept; there is no way to attach the value, so it cannot leak from the error later. A template shows `[redacted]` in its place. |

`Error.NotFound("Customer", "Email", email)` keeps the looked-up value as `Data` and leaves it out of the description. A
template can name a `Data` placeholder only if it was declared with `showData: true`, and then the value is shown as one short
line (control characters replaced, 64 characters at most). `ToString()` on an error, an argument and the argument list
shows names only, never values.

What the package leaves to you: it does not translate and it does not scan free text for personal data. A translation layer
looks an error up by `Code`, then by `Arguments.Template`, and falls back to `Description`; only the human text is ever
translated, never the code.

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
- **Errors compare by value**, arguments included: two errors made separately from the same template with the same
  arguments are equal.
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
