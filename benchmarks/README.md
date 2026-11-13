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

A result is a struct, so the allocation column is expected to be empty, except where a lambda captures a variable. Several of
these operations take a nanosecond or two, which BenchmarkDotNet can only estimate: read them as "a few nanoseconds", not as
exact ratios against the bare value, which the compiler folds away.
