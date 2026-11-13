# Sondor.Options

Options bound from a configuration section by convention and validated when the host starts. A section can be **required**
or **optional**, so a feature can switch on by being configured. It is the standard `IOptions` pattern with a few decisions
made for you, so every service in a fleet configures itself the same way.

```bash
dotnet add package Sondor.Options
```

```csharp
services.AddSondorOptions<DatabaseOptions>()                       // section "DatabaseOptions", required
    .Validate(options => options.Timeout > 0, "Timeout must be positive.");

services.AddSondorOptions<LokiOptions>("Logging:Loki", SectionRequirement.Optional);   // on when "Logging:Loki" is configured
```

`AddSondorOptions` returns the framework's `OptionsBuilder<T>`, so `Validate`, `ValidateDataAnnotations`, `PostConfigure` and the
rest chain as usual. For FluentValidation use `Sondor.Options.FluentValidation`. The options type needs a public parameterless
constructor (the framework creates it, then binds the section onto it); `init` properties and records with them work.

## What it does

- The section is named after the type unless you name it. `Messaging:Broker` style paths work.
- **The host does not start with bad options.** Validation runs at startup (`ValidateOnStart`), not at first use.
- **A required section that is missing fails with its name** (`The configuration section 'DatabaseOptions' is required ...`),
  instead of silently binding a default object.
- `IOptions<T>`, `IOptionsSnapshot<T>` and `IOptionsMonitor<T>` are all available and behave as documented by Microsoft.

## Optional sections: opt in by configuration

A section is **configured** when it has at least one key or value. `"Feature": {}` is not; `"Feature": { "Enabled": true }` or
the environment variable `Feature__Enabled=true` is.

| | Required | Optional |
|---|---|---|
| Section missing | Startup fails, naming the section | Nothing fails; no validation runs; options are a default instance |
| Section there, invalid | Startup fails | Startup fails: switching a feature on with a bad value is an error |

Two ways to use it, for two questions.

**Should this feature's services exist at all?** Ask while registering:

```csharp
if (builder.Configuration.HasSondorSection<LokiOptions>("Logging:Loki"))
{
    services.AddSondorOptions<LokiOptions>("Logging:Loki");
    services.AddLokiSink();
}
```

This reads the configuration once, at startup.

**Is it on right now?** Register it as optional and ask `ISondorOptionsState<T>`, which reads the live configuration:

```csharp
public sealed class Sink(IOptionsMonitor<LokiOptions> options, ISondorOptionsState<LokiOptions> state)
{
    public void Write(string line)
    {
        if (!state.IsConfigured) return;      // the feature is off
        // use options.CurrentValue
    }
}
```

## Configuration that changes while the service runs

Azure App Configuration, a mounted file or any provider that reloads works: the options follow through `IOptionsMonitor<T>`
(`CurrentValue`, `OnChange`) and through `IOptionsSnapshot<T>` (the next scope). With Azure App Configuration, register the
sentinel key as the refresh trigger as usual (`ConfigureRefresh(refresh => refresh.Register("Sentinel", refreshAll: true))`):
when the sentinel changes, the provider reloads, and the options are rebuilt and validated once for that change.

**A bad change does not take the service down.** The framework's behaviour is that, after a reload into invalid options,
`IOptionsMonitor<T>.CurrentValue` throws for every reader until someone fixes the configuration, and the exception also surfaces
from the code that triggered the refresh. Here, by default, an invalid change is **rejected**: the options last found valid stay
in use, and a warning is logged with the reason (category `Sondor.Options`, never the values). The next valid change applies.
That covers a failed validator, a value that does not convert (`"ten"` for an `int`) and a required section that disappears.

```csharp
services.AddSondorOptions<DatabaseOptions>(onInvalidReload: InvalidReloadBehavior.Fail);   // the framework's behaviour
```

Startup is always strict, whichever you choose: there is nothing valid to keep yet. Watch for the warning in your logs, because a
rejected change means the service is running on older settings than the store holds.

`IOptions<T>` is fixed when first read, as always; use `IOptionsMonitor<T>` or `IOptionsSnapshot<T>` for settings that should change.

## Notes

- Named options of the same type (`services.Configure<T>("name", ...)`) are not tied to the section and behave as the framework's do.
- Calling `AddSondorOptions` twice for one type: the last call decides the requirement and the reload behaviour.
- Depends only on Microsoft's `Microsoft.Extensions.Options.ConfigurationExtensions` and `Logging.Abstractions`.
