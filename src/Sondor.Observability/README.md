# Sondor.Observability

OpenTelemetry for services, **configured from configuration**: traces, metrics and logs with one consistent resource, a required
service name, safe defaults, and sampling and signals you can change **while the service runs**. OTLP export is built in; any
other exporter plugs in through the builder.

```bash
dotnet add package Sondor.Observability
```

```csharp
builder.Services.AddSondorObservability("orders", observability => observability
    .AddSource("Sondor.Cqrs")      // listen to the sources and meters you want: yours, a library's
    .AddMeter("Sondor.Cqrs"));
```

```json
{
  "Observability": {
    "Otlp": { "Endpoint": "http://collector:4317" },
    "Traces": { "SamplingRatio": 0.1 }
  }
}
```

That is the whole setup. Until an endpoint is configured nothing leaves the process, so the same code runs in tests and on a
laptop without a collector.

## Why use it

The OpenTelemetry SDK already does the work, and a single service can wire it up in a few lines. This package is for the next
step: **many services, one setup, controlled from configuration**.

- **Change cost without a deployment.** Sampling ratio, each signal's mode and noisy sources reload live (an Azure App
  Configuration refresh, a changed file). Turn traces to 1% during a quiet week, or logs to errors only, with a configuration
  change.
- **A bad change cannot take the service down.** Settings are validated at startup, and an invalid reload is rejected: the last
  valid settings stay in use and a warning is logged (it is built on [`Sondor.Options`](../Sondor.Options)).
- **No anonymous telemetry.** The service name is a required parameter, and all three signals carry the same resource
  (name, version, environment, one instance id).
- **Safe by default.** Nothing is instrumented until you ask, so nothing captures SQL text or URL query strings unless you
  choose to. Header values (usually API keys) are never logged or put in an error message.
- **Adopt it one signal at a time.** Every signal has its own mode, and you can keep your existing exporter next to it.
- **No lock-in.** It registers no source or meter of its own, and nothing is named after this package. You can reach the
  OpenTelemetry builder for anything else.

If you run one service and are happy with a handful of lines of OpenTelemetry setup, you do not need this package.

## Settings

Section `Observability` (or the one you name). Everything is optional.

| Key | Meaning | Reloads |
|---|---|---|
| `Traces:Mode`, `Metrics:Mode`, `Logs:Mode` | `On` (default), `ErrorsOnly` or `Off`. Metrics have no error level, so they are `On` or `Off` only. | live |
| `Traces:SamplingRatio` | Share of new traces kept while the mode is `On`: `0` to `1`. Default `0.1`. | live |
| `Traces:ExcludeSources` | Activity sources whose spans are not exported. | live |
| `Version`, `Environment`, `InstanceId` | Resource. Defaults: the entry assembly's version, the host's environment name, a new GUID per process. | startup |
| `ResourceAttributes:<key>` | Extra resource attributes. | startup |
| `Diagnostics:Level` | What the SDK's own diagnostics are logged at: `Warning` (default), `Debug` for everything, `None` for off. See [When traces do not arrive](#when-traces-do-not-arrive). | live |
| `Otlp:Endpoint`, `Protocol`, `Headers`, `HeadersFile`, `Timeout` | OTLP export. `Protocol` is `Grpc` (default) or `HttpProtobuf`. | startup |

What each mode does:

| | `On` | `ErrorsOnly` | `Off` |
|---|---|---|---|
| Traces | Sampled at the ratio | Spans that ended with an error status | Nothing; spans are not even recorded |
| Metrics | Exported | not available | Nothing is exported |
| Logs | Exported | Error and above | Nothing is exported |

**Sampling is parent-based.** A trace that arrives with its caller's decision follows it, so a trace is never half sampled by
two of your services. `ErrorsOnly` for traces keeps each failed span but not its siblings, so a trace can arrive incomplete. To
keep whole traces that contain an error, use tail sampling in the collector.

The standard OpenTelemetry variables are honoured when the section does not set the value: `OTEL_EXPORTER_OTLP_ENDPOINT`,
`OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc` or `http/protobuf`), `OTEL_EXPORTER_OTLP_HEADERS` and `OTEL_EXPORTER_OTLP_TIMEOUT`
(milliseconds).

### Environment variables

The default hosts (`WebApplication.CreateBuilder`, `Host.CreateApplicationBuilder`) already read environment variables into
configuration, so `Observability__Otlp__Endpoint=http://collector:4317` works with no code. With a bare `ConfigurationBuilder`,
call `AddEnvironmentVariables()`.

### Secrets

OTLP headers usually hold a key. Give them as a value, or point at a file:

```json
{ "Observability": { "Otlp": { "HeadersFile": "/run/secrets/otlp_headers" } } }
```

A `Headers` value that is the path of a file under `/run/secrets` (a Docker secret) is read from the file too. The file holds
`key=value[,key2=value2]`. Secrets are read at startup; rotating one takes a restart.

## In code

```csharp
builder.Services.AddSondorObservability("orders", observability => observability
    .WithVersion("2.3.1")                   // code wins over configuration, which wins over the defaults
    .WithEnvironment("Production")
    .WithInstanceId(Environment.MachineName)
    .WithResourceAttribute("team", "payments")
    .AddSource("Orders.Domain")
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation())   // any OpenTelemetry instrumentation package
    .WithMetrics(metrics => metrics.AddRuntimeInstrumentation()));
```

`WithTracing`, `WithMetrics` and `WithLogging` hand you the SDK's builders, and `observability.OpenTelemetry` is the SDK's own
`OpenTelemetryBuilder`. An exporter you add that way (Prometheus, Azure Monitor, a vendor's) is subject to the sampler, so the
ratio and `Off` apply to it; the modes that filter at export (`ErrorsOnly`, `ExcludeSources`) apply to the OTLP exporter only.

### Moving from separate tools

You do not have to move everything at once. A typical path:

1. Add the package with no `Otlp` endpoint: every service gets the same resource, sampling and signal switches in place,
   and your current exporters (added through the builder) keep running.
2. Send one signal over OTLP (for example traces) and leave the others where they are, using each signal's mode.
3. When ready, send everything over OTLP to a collector and let the collector fan out to your backends.

## When traces do not arrive

The OpenTelemetry SDK reports its own problems (a refused connection, a rejected key, a full queue) only through internal
event sources, which is why they are so easy to miss. This package turns them into ordinary logs, and says what it set up.

- **SDK diagnostics are logged, by default at `Warning`.** A failing export shows up under the logger category
  `Sondor.Observability.Diagnostics`, for example `OpenTelemetry-Exporter-OpenTelemetryProtocol: ... connection refused`.
  Set `Observability:Diagnostics:Level` to `Debug` to see everything the SDK reports, or `None` to switch it off. It reloads
  live, so you can raise it on a running service while you chase a problem. These logs go to your other log providers (console,
  files) and **never to the OTLP log exporter**: a failing export would otherwise log about itself for ever.
- **A start-up summary is logged once** (category `Sondor.Observability`, Information): the service name, each signal's mode,
  the sampling ratio, whether OTLP is configured and where it sends (the address only, never credentials or headers), and the
  sources and meters being listened to. Most "no traces" cases are visible there: a source that was never added with
  `AddSource`, a signal that is `Off`, or no endpoint.
- **A change to the live settings is logged** when a reload alters a mode, the ratio, the excluded sources or the diagnostics
  level, so a cost change made in your configuration store leaves a trail. A reload that is rejected as invalid is logged by
  `Sondor.Options`.

The SDK's own `OTEL_DIAGNOSTICS.json` file still works if you prefer it.

## Logs

Logs go through the standard `ILogger` pipeline, with scopes and the formatted message included, so nothing replaces your
logging setup. Level filtering is the framework's own: `Logging:LogLevel`, or `Logging:OpenTelemetry:LogLevel` for the
exported copy only.

## Notes

- Needs `net10.0`. Depends on the OpenTelemetry SDK (`OpenTelemetry.Extensions.Hosting`, the OTLP exporter) and `Sondor.Options`.
- Modes, the sampling ratio and excluded sources read the live configuration. The exporter's endpoint, protocol and headers
  are fixed when the host starts.
- With `Metrics:Mode` set to `Off`, metrics are still collected in process; only the export is skipped.
- Calling `AddSondorObservability` once per host is the supported use.
