using System.Diagnostics;

namespace Sondor.Observability.Tests;

/// <summary>Settings that change while the service runs, the way a configuration store refresh changes them.</summary>
public class LiveSettingsTests
{
    [Test]
    public async Task The_sampling_ratio_follows_a_reload()
    {
        var sourceName = Names.Unique("live.ratio");
        using var source = new ActivitySource(sourceName);
        var configuration = new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "0"));

        await using var running = await Running.Start(configuration, builder => builder.AddSource(sourceName));

        var before = Sources.Span(source, "ratio-before");
        configuration.Replace(("Observability:Traces:SamplingRatio", "1"));
        var after = Sources.Span(source, "ratio-after");

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.False, "a ratio of 0 keeps nothing");
            Assert.That(after, Is.True, "a ratio of 1 keeps everything, from the very next span");
        });
    }

    [Test]
    public async Task Turning_traces_off_and_on_applies_without_a_restart()
    {
        var sourceName = Names.Unique("live.mode");
        using var source = new ActivitySource(sourceName);
        var configuration = new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "1"));

        await using var running = await Running.Start(configuration, builder => builder.AddSource(sourceName));

        var on = Sources.Span(source, "mode-on");
        configuration.Replace(("Observability:Traces:Mode", "Off"), ("Observability:Traces:SamplingRatio", "1"));
        var off = Sources.Span(source, "mode-off");
        configuration.Replace(("Observability:Traces:SamplingRatio", "1"));
        var onAgain = Sources.Span(source, "mode-on-again");

        Assert.That((on, off, onAgain), Is.EqualTo((true, false, true)));
    }

    [Test]
    public async Task A_bad_reload_keeps_the_last_valid_settings()
    {
        var sourceName = Names.Unique("live.invalid");
        using var source = new ActivitySource(sourceName);
        var configuration = new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "1"));

        await using var running = await Running.Start(configuration, builder => builder.AddSource(sourceName));

        configuration.Replace(("Observability:Traces:SamplingRatio", "7"));
        var kept = Sources.Span(source, "invalid-kept");

        configuration.Replace(("Observability:Traces:SamplingRatio", "0"));
        var applied = Sources.Span(source, "invalid-then-valid");

        Assert.That((kept, applied), Is.EqualTo((true, false)));
    }

    [Test]
    public async Task The_logs_mode_follows_a_reload()
    {
        using var receiver = new OtlpReceiver();
        var configuration = new ReloadableConfiguration().With(
            ("Observability:Otlp:Endpoint", receiver.Endpoint.ToString()),
            ("Observability:Otlp:Protocol", "HttpProtobuf"));

        await using var running = await Running.Start(configuration);

        running.Error("log-before");
        running.Flush();

        configuration.Replace(
            ("Observability:Otlp:Endpoint", receiver.Endpoint.ToString()),
            ("Observability:Otlp:Protocol", "HttpProtobuf"),
            ("Observability:Logs:Mode", "Off"));

        running.Error("log-after");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/logs", "log-before"), Is.True);
            Assert.That(receiver.Received("/v1/logs", "log-after"), Is.False);
        });
    }
}
