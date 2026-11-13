# Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org) projects, one for each package that has something worth measuring. They are run by
hand, in Release, never in CI (CI only checks that they build), and no results are committed: numbers depend on the machine, so
run them on yours.

```bash
dotnet run -c Release --project benchmarks/Sondor.Cqrs.Benchmarks -- --filter "*"
```

Pick one scenario with a filter, for example `--filter "*Benchmarks.RequestBenchmarks.*"`. Close other programs first.

## Sondor.Cqrs

| Class | Measures |
|---|---|
| `RequestBenchmarks` | `Send` against resolving the handler from the container and calling it; with three pass-through behaviours; with the built-in tracing, metrics and logging while nothing listens. |
| `ObservedRequestBenchmarks` | The built-in tracing and metrics while a listener records every span and measurement. Its own class, because listeners are process-wide. |
| `NotificationBenchmarks` | `Publish` to three handlers. |

The handlers do no work, so what is measured is the library. Read the allocation column as much as the time: a request
allocates its handler, the instance of each behaviour in its pipeline (behaviours are built for every request, so they can use
scoped services), and one pipeline object when there is at least one behaviour.

Compare against another library on your own machine by adding it to a scratch project that references `Sondor.Cqrs`. This
repository does not reference other mediator libraries.

## Sondor.Results

| Class | Measures |
|---|---|
| `CreateAndReadBenchmarks` | Making a result and reading it (`Success`, the implicit conversions, `Failure`, `TryGetValue`), against a bare value, and reading through `IResultState` by a constrained generic call and by an interface variable. |
| `ChainingBenchmarks` | `Ensure`, `Map`, `Bind` and `Tap` on a success and on a failure, with static lambdas, and with a lambda that captures a variable (which allocates in the caller). |
| `FailureBenchmarks` | The same expected failure returned as a value and thrown and caught as an exception. |
| `ErrorCreationBenchmarks` | Making an error on the failure path: the constructor, a built-in factory, a template with a code of its own and one declared by the application, against the constructor as the floor. These allocate (the arguments and the formatted description); the point is that it is a few small objects, and that the success path pays nothing. |

A result is a struct, so the allocation column is expected to be empty, except where a lambda captures a variable. Several of
these operations take a nanosecond or two, which BenchmarkDotNet can only estimate: read them as "a few nanoseconds", not as
exact ratios against the bare value, which the compiler folds away.

## Sondor.Options

| Class | Measures |
|---|---|
| `BindAndValidateBenchmarks` | Registering, building and first reading options, and every later read (`IOptions`, `IOptionsMonitor`, `IOptionsSnapshot` per scope, an optional section that is absent), each against the framework's own `AddOptions().BindConfiguration().ValidateOnStart()`. |
| `ReloadBenchmarks` | One configuration change (what an Azure App Configuration refresh causes) rebuilding the options, against the framework, and a change that is rejected in favour of the last valid options. Every case runs after 0 and after 10,000 earlier changes: a cost that grows with the changes already seen would show as a difference between the two. |

Sondor.Options replaces the options factory, so what has to hold is that a read costs what the framework's costs and allocates
nothing, and that a change costs the same however many came before it.

## Sondor.Options.FluentValidation

| Class | Measures |
|---|---|
| `ValidationBenchmarks` | A configuration change, and a snapshot built in a new scope, validated with FluentValidation against the same three checks written as a `Validate` delegate. Each validation resolves the validator from a scope of its own, so the question is what that costs and that it does not grow: every case runs after 0 and after 10,000 earlier changes. |

## Sondor.Observability

| Class | Measures |
|---|---|
| `SpanBenchmarks` | Starting and ending one span with the OpenTelemetry SDK set up by hand, against Sondor.Observability with a sampling ratio of 1, with a ratio of 0, and with traces off. Nothing is exported, so this is the sampler and listener cost every span pays. |
| `ReloadBenchmarks` | One sampling ratio change, and one span afterwards. Every case runs after 0 and after 10,000 earlier changes: a listener or a sampler left behind by each change would show as a difference between the two. |

## Sondor.Results.Localization

| Class | Measures |
|---|---|
| `LocalizeBenchmarks` | Turning an error into text, which happens on the failure path only: the English text (no lookup), a French error found by its general template (a sentence, a title and two words of vocabulary), one found by its own code, one with no translation at all (every layer asked, then English), and the overload that follows the current culture, against reading the description the error already carries. The store is a dictionary, so this is the localizer and not a file or a database. |

A translated lookup is a few hundred nanoseconds and a handful of small allocations, on a path that already built an error, so
read it against the cost of the failure it describes rather than against the floor.
