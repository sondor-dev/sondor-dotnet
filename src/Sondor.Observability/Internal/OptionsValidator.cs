using Microsoft.Extensions.Options;

namespace Sondor.Observability.Internal;

/// <summary>Checks the settings when the host starts, and when a reload comes in. Messages never carry a header value.</summary>
internal sealed class OptionsValidator(Ambient ambient) : IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityOptions options)
    {
        var problems = new List<string>();

        if (!Enum.IsDefined(options.Traces.Mode) || !Enum.IsDefined(options.Logs.Mode) || !Enum.IsDefined(options.Metrics.Mode))
        {
            problems.Add("A signal mode must be On, ErrorsOnly or Off.");
        }

        if (options.Metrics.Mode == SignalMode.ErrorsOnly)
        {
            problems.Add("Metrics cannot be ErrorsOnly: a metric has no error level. Use On or Off.");
        }

        if (!(options.Traces.SamplingRatio is >= 0 and <= 1))
        {
            problems.Add("Traces:SamplingRatio must be between 0 and 1.");
        }

        if (options.Traces.ExcludeSources.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("Traces:ExcludeSources must not hold an empty name.");
        }

        if (options.ResourceAttributes.Keys.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add("ResourceAttributes must not hold an empty key.");
        }

        _ = OtlpSettings.Resolve(options.Otlp, ambient, problems);

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
