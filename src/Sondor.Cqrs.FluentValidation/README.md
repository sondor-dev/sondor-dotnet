# Sondor.Cqrs.FluentValidation

[FluentValidation](https://docs.fluentvalidation.net) for [`Sondor.Cqrs`](../Sondor.Cqrs). It runs every validator of a
request before its handler, so a request is validated the same way whatever sent it (HTTP, a message, background work), and an
invalid request never reaches the handler.

It is a separate package so that `Sondor.Cqrs` itself has no third-party dependency. If you move to another validation
library, remove this package and write a behaviour for it; nothing else changes.

```bash
dotnet add package Sondor.Cqrs.FluentValidation
```

```csharp
services.AddValidatorsFromAssemblyContaining<RegisterNodeValidator>();   // FluentValidation.DependencyInjectionExtensions
services.AddCqrs(cqrs =>
{
    cqrs.AddHandlersFromAssemblyContaining<RegisterNodeHandler>();
    cqrs.UseTracing();
    cqrs.UseFluentValidation();        // after what should see refused requests, before the handler
});

public sealed class RegisterNodeValidator : AbstractValidator<RegisterNode>
{
    public RegisterNodeValidator() => RuleFor(command => command.Name).NotEmpty().WithMessage("A name is required.");
}
```

The package does not register your validators. Register them yourself, for example with `AddValidatorsFromAssemblyContaining`
from `FluentValidation.DependencyInjectionExtensions`. Every `IValidator<TRequest>` found in the container runs, one after
another.

## What happens to an invalid request

It depends on whether the response type can say "refused":

| Response type | An invalid request |
|---|---|
| Has an `IFailureFactory<TResponse>` registered | Returns the failure it builds, with the code `Validation.Failed` (`CqrsValidation.ErrorCode`), kind `Validation`, and the validators' messages, each once, joined with a space. |
| Has none | Throws FluentValidation's `ValidationException`, which carries every failure. |

With [`Sondor.Cqrs.Results`](../Sondor.Cqrs.Results) and `services.AddCqrsResults()`, a request that returns `Result` or
`Result<T>` gets a failed result with an error of type `Validation`. For another response type, register your own factory:

```csharp
public sealed class ReplyFailureFactory : IFailureFactory<Reply>
{
    public Reply Create(RequestFailure failure) => new(false, failure.Code, failure.Description);
}
```

## Things that behave in a specific way

- **The messages are your validators' messages.** FluentValidation's default messages for some rules include the rejected
  value (for example "You entered 15"). If a message can reach a client, write your own with `WithMessage`, or leave the value
  out of it. The package adds nothing from the request.
- **The failure carries the messages of each property.** A failure factory also receives `RequestFailure.Errors` (property name, as the validator reported it, to its messages, each once), so a response can keep the breakdown that the joined description loses. It needs `Sondor.Cqrs` 0.2.1 or later.
- **A span is tagged, never the values.** When the request is traced, `cqrs.validation.properties` lists the names of the
  properties that were refused and `cqrs.validation.error_count` how many failures there were.
- **A request with no validator costs nothing.** It goes straight on.
- **Validators run asynchronously.** `ValidateAsync` is used, so async rules (`MustAsync`, `CustomAsync`) work, and the request's
  cancellation token reaches them.
- **Where you put `UseFluentValidation()` matters.** Behaviours run in the order added, the first one outermost. Put things that
  should see every attempt, such as auditing, before it.
