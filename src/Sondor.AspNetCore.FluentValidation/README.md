# Sondor.AspNetCore.FluentValidation

[FluentValidation](https://docs.fluentvalidation.net) for [`Sondor.AspNetCore`](../Sondor.AspNetCore). When a
`ValidationException` leaves a request, the problem response is a `400` with the usual `errorCode` and `traceId` and an
`errors` member that lists the messages of each property.

It is a separate package so that `Sondor.AspNetCore` itself has no third-party dependency.

```bash
dotnet add package Sondor.AspNetCore.FluentValidation
```

```csharp
builder.Services.AddSondorProblemDetails();
builder.Services.AddSondorFluentValidationProblems();

app.UseSondorProblemDetails();
```

```json
{
  "status": 400,
  "title": "Bad Request",
  "detail": "One or more validation errors occurred.",
  "errorCode": "Validation.Failed",
  "traceId": "00-...",
  "errors": {
    "name": ["A name is required."],
    "address.postCode": ["The post code is not valid."],
    "items[0].unitPrice": ["The price must be positive."]
  }
}
```

## Where the exception comes from

Anything that throws FluentValidation's `ValidationException`: your own `ValidateAndThrowAsync`, or
[`Sondor.Cqrs.FluentValidation`](../Sondor.Cqrs.FluentValidation) for a request whose response type has no failure factory.

## Things that behave in a specific way

- **Property names follow the API's JSON naming policy** (`ConfigureHttpJsonOptions`, camelCase by default), so they match the
  properties of the request body. Each name in a path is converted (`Items[0].UnitPrice` becomes `items[0].unitPrice`); the
  indexer is kept. With the policy set to `null` the names are left as FluentValidation reports them.
- **Several messages for one property** are listed in the order they were reported, each once. A failure with no property
  name is listed under the empty name `""`.
- **The code is `Validation.Failed`** (`FluentValidationProblemsExtensions.ErrorCode`), the same code
  `Sondor.Cqrs.FluentValidation` uses. The status is the one of `ErrorType.Validation` in `ErrorResponseOptions.StatusByType`
  (400 by default); set it to 422 if you prefer.
- **The messages are your validators' messages**, sent to the caller as they are. FluentValidation's default message for some
  rules includes the rejected value, so write your own with `WithMessage` where that matters.
- **A failed `Result` has no `errors`.** An `Error` carries a code, a description and a type, not a list of properties, so
  there is nothing to list. To get `errors`, let the `ValidationException` escape.
- **Your own mapping wins.** The package maps `ValidationException` through `ErrorResponseOptions.Exceptions`; a mapping you
  add afterwards replaces it, and the `errors` member is still added.
- **Calling it twice is harmless.** Nothing is registered a second time.
