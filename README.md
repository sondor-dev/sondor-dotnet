# sondor-dotnet

Small, focused .NET libraries for building services: results and errors, in-process commands and queries, validated
options, observability, ASP.NET Core error handling and HTTP clients. Each package does one job, depends on as little as possible, and
is released on its own.

[![CI](https://github.com/sondor-dev/sondor-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/sondor-dev/sondor-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> **Status:** early. Nothing is published to nuget.org yet; the first package is being built. The table below is the plan,
> not a promise of dates.

## Packages

| Package | What it is | Status |
|---|---|---|
| [`Sondor.Results`](src/Sondor.Results) | `Result`, `Result<T>` and `Error`: failures as values, with stable `Area.Reason` codes | in development |
| [`Sondor.Results.Localization`](src/Sondor.Results.Localization) | Translated title and message for an `Error`, from your own resources (resx, JSON, a database), with the English text as the fallback and missing translations made visible | in development |
| [`Sondor.Options`](src/Sondor.Options) | Options bound from configuration by convention and validated at start-up; required or optional sections; a bad configuration reload keeps the last valid options | in development |
| [`Sondor.Options.FluentValidation`](src/Sondor.Options.FluentValidation) | FluentValidation for `Sondor.Options`: validates options at start-up and on every configuration reload | in development |
| [`Sondor.Cqrs`](src/Sondor.Cqrs) | In-process commands, queries and notifications with an opt-in pipeline (tracing, metrics, logging). No third-party dependencies | in development |
| [`Sondor.Cqrs.Results`](src/Sondor.Cqrs.Results) | `Sondor.Results` support for `Sondor.Cqrs`: failed results show up as failures in traces, metrics and logs | in development |
| [`Sondor.Cqrs.FluentValidation`](src/Sondor.Cqrs.FluentValidation) | FluentValidation for `Sondor.Cqrs`: validates every request before its handler | in development |
| [`Sondor.AspNetCore`](src/Sondor.AspNetCore) | One consistent RFC 9457 problem response for an ASP.NET Core API: a failed `Result`, an unhandled exception and the framework's own bare 401 or 404 share one shape with a stable `errorCode` and a trace id. Minimal APIs and MVC | in development |
| [`Sondor.AspNetCore.FluentValidation`](src/Sondor.AspNetCore.FluentValidation) | FluentValidation for `Sondor.AspNetCore`: a `ValidationException` becomes a 400 problem with a per-property `errors` member | in development |
| [`Sondor.Observability`](src/Sondor.Observability) | OpenTelemetry for services from configuration: traces, metrics and logs with one resource, OTLP export, and sampling and signals you can change while the service runs | in development |
| [`Sondor.Http`](src/Sondor.Http) | Read the answer of another HTTP service as a value: a failed status, a problem document, a timeout and an unreachable host become a failed `Result` with a stable error code, never an exception. Reads the `Sondor.AspNetCore` problems back, with bounded reads, per-call timeouts, an opt-in bearer token that only goes to allowed hosts, and a hook for other error formats. Built on `HttpClient` and `IHttpClientFactory` | in development |

## How the packages are put together

- **Layers go one way.** A package depends only on packages in a lower layer. Optional dependencies (FluentValidation, Azure,
  EF Core) live in their own adapter packages, so using one library never drags in a framework you did not choose.
- **No hidden coupling.** Packages are registered explicitly. Nothing configures itself by scanning, building a second
  service provider, or reading global state.
- **.NET 10.** Targeting one framework keeps the packages cheap to maintain and test. Older runtimes are not supported.
- **The public API is a promise.** It is tracked in each package's `PublicAPI.*.txt` files and checked against the last
  release; a breaking change is a major version.

## Building

You need the .NET SDK pinned in [`global.json`](global.json).

```bash
dotnet build -c Release
dotnet test -c Release
```

## Contributing

Issues and pull requests are welcome; read [CONTRIBUTING.md](CONTRIBUTING.md) first. To report a vulnerability, see
[SECURITY.md](SECURITY.md).

## Licence

[MIT](LICENSE). The licence covers the code. The Sondor name and logo (`assets/`) are not part of it: you may use them to say that you
use these packages, but not to suggest that a fork or a derived product is the official one.
