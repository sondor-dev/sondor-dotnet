# sondor-dotnet

Small, focused .NET libraries for building services: results and errors, in-process commands and queries, validated
options, observability and ASP.NET Core error handling. Each package does one job, depends on as little as possible, and
is released on its own.

[![CI](https://github.com/sondor-dev/sondor-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/sondor-dev/sondor-dotnet/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> **Status:** early. Nothing is published to nuget.org yet; the first package is being built. The table below is the plan,
> not a promise of dates.

## Packages

| Package | What it is | Status |
|---|---|---|
| [`Sondor.Results`](src/Sondor.Results) | `Result`, `Result<T>` and `Error`: failures as values, with stable `Area.Reason` codes | in development |
| `Sondor.Options` | Options bound from configuration, validated at start-up | planned |
| [`Sondor.Cqrs`](src/Sondor.Cqrs) | In-process commands, queries and notifications with an opt-in pipeline (tracing, metrics, logging). No third-party dependencies | in development |
| `Sondor.AspNetCore` | `Result` to RFC 9457 problem details, with `errorCode` and `traceId` | planned |
| `Sondor.Observability` | OpenTelemetry setup for services | planned |
| `Sondor.Http` | Typed HTTP clients with resilience, returning `Result` | planned |

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
