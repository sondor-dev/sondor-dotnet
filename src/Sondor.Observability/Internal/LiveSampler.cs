using OpenTelemetry.Trace;

namespace Sondor.Observability.Internal;

/// <summary>A sampler that asks the live settings each time, so a changed ratio or mode applies to the next trace.</summary>
internal sealed class LiveSampler(LiveSettings settings) : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        settings.Current.Sampler.ShouldSample(in samplingParameters);
}
