# Sondor.Options.FluentValidation

[FluentValidation](https://docs.fluentvalidation.net) for [`Sondor.Options`](../Sondor.Options). It validates options with the
`IValidator` of their type when the host starts, and again each time the configuration reloads.

It is a separate package so that `Sondor.Options` has no third-party dependency. Use `Validate`, `ValidateDataAnnotations` or
your own `IValidateOptions` instead and you do not need it.

```bash
dotnet add package Sondor.Options.FluentValidation
```

```csharp
services.AddSondorOptions<DatabaseOptions>()
    .ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>();     // registers the validator and validates with it

public sealed class DatabaseOptionsValidator : AbstractValidator<DatabaseOptions>
{
    public DatabaseOptionsValidator()
    {
        RuleFor(options => options.ConnectionString).NotEmpty();
        RuleFor(options => options.Timeout).InclusiveBetween(1, 300);
    }
}
```

If you register validators yourself (for example with `AddValidatorsFromAssemblyContaining`), use `ValidateFluently()` without the
second type: it validates with whatever `IValidator<DatabaseOptions>` is in the container.

## What you get

- **A failure reads as** `Validation failed for DatabaseOptions.Timeout with the error: ...`, one for each failing rule, so the
  host's start-up error lists everything that is wrong at once.
- **Only the validator you ask for is registered.** Nothing scans an assembly, so a validator you did not ask for never appears in
  the container (Sondor.Options 1.x registered every validator of the options' assembly).
- **Scoped dependencies work.** Each validation resolves the validator from a scope of its own, which is disposed straight after.
- **With an optional section** (`SectionRequirement.Optional`) that is not configured, the validator does not run: a feature that
  is off has nothing to validate. Once the section is there, it must be valid.
- **On a reload** it behaves as the rest of `Sondor.Options`: an invalid change is rejected and the last valid options stay in
  use, unless you chose `InvalidReloadBehavior.Fail`.
- Works on the framework's own builder too (`services.AddOptions<T>().ValidateFluently()`); validation then applies to the
  options of the name the builder was made for.

## Limits

Only synchronous rules can be used. Options are built synchronously, so FluentValidation throws when it meets an asynchronous
rule (`MustAsync`, `CustomAsync`), which fails the start. Keep option validation to the values themselves: a check that needs a
network call does not belong in options validation.

If no `IValidator<T>` is in the container, the first validation fails with a message that says so.
