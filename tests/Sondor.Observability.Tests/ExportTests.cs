using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Observability.Tests;

/// <summary>What actually leaves the process, seen by a receiver speaking OTLP over HTTP.</summary>
public class ExportTests
{
    private static ReloadableConfiguration Configured(OtlpReceiver receiver, params (string Key, string? Value)[] more) =>
        new ReloadableConfiguration().With(
        [
            ("Observability:Otlp:Endpoint", receiver.Endpoint.ToString()),
            ("Observability:Otlp:Protocol", "HttpProtobuf"),
            ("Observability:Traces:SamplingRatio", "1"),
            .. more,
        ]);

    [Test]
    public async Task Traces_metrics_and_logs_reach_their_own_paths()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.all");
        var meterName = Names.Unique("export.all");
        using var source = new ActivitySource(sourceName);
        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("things");

        await using var running = await Running.Start(Configured(receiver), builder => builder.AddSource(sourceName).AddMeter(meterName));

        Sources.Span(source, "span-all");
        counter.Add(1);
        running.Error("log-all");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/traces", "span-all"), Is.True);
            Assert.That(receiver.Received("/v1/traces", "test-service"), Is.True, "the resource travels with the spans");
            Assert.That(receiver.Received("/v1/metrics", "things"), Is.True);
            Assert.That(receiver.Received("/v1/logs", "log-all"), Is.True);
        });
    }

    [Test]
    public async Task Nothing_is_exported_without_an_endpoint()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.none");
        using var source = new ActivitySource(sourceName);

        await using var running = await Running.Start(new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "1")), builder => builder.AddSource(sourceName));

        Sources.Span(source, "span-none");
        running.Flush();

        Assert.That(receiver.Requests, Is.Empty);
    }

    [Test]
    public async Task A_signal_that_is_off_exports_nothing_while_the_others_do()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.off");
        var meterName = Names.Unique("export.off");
        using var source = new ActivitySource(sourceName);
        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("off-things");

        await using var running = await Running.Start(
            Configured(receiver, ("Observability:Traces:Mode", "Off"), ("Observability:Metrics:Mode", "Off")),
            builder => builder.AddSource(sourceName).AddMeter(meterName));

        Sources.Span(source, "span-off", fail: true);
        counter.Add(1);
        running.Error("log-still-on");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.To("/v1/traces"), Is.Empty);
            Assert.That(receiver.To("/v1/metrics"), Is.Empty);
            Assert.That(receiver.Received("/v1/logs", "log-still-on"), Is.True);
        });
    }

    [Test]
    public async Task Errors_only_keeps_failed_spans_and_error_logs()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.errors");
        using var source = new ActivitySource(sourceName);

        await using var running = await Running.Start(
            Configured(receiver, ("Observability:Traces:Mode", "ErrorsOnly"), ("Observability:Logs:Mode", "ErrorsOnly")),
            builder => builder.AddSource(sourceName));

        Sources.Span(source, "span-fine");
        Sources.Span(source, "span-failed", fail: true);
        running.Warning("log-warning");
        running.Error("log-error");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/traces", "span-failed"), Is.True);
            Assert.That(receiver.Received("/v1/traces", "span-fine"), Is.False);
            Assert.That(receiver.Received("/v1/logs", "log-error"), Is.True);
            Assert.That(receiver.Received("/v1/logs", "log-warning"), Is.False);
        });
    }

    [Test]
    public async Task A_logs_mode_of_off_exports_no_logs()
    {
        using var receiver = new OtlpReceiver();

        await using var running = await Running.Start(Configured(receiver, ("Observability:Logs:Mode", "Off")));

        running.Error("log-silenced");
        running.Flush();

        Assert.That(receiver.To("/v1/logs"), Is.Empty);
    }

    [Test]
    public async Task Excluded_sources_are_not_exported()
    {
        using var receiver = new OtlpReceiver();
        var noisy = Names.Unique("export.noisy");
        var useful = Names.Unique("export.useful");
        using var noisySource = new ActivitySource(noisy);
        using var usefulSource = new ActivitySource(useful);

        await using var running = await Running.Start(
            Configured(receiver, ("Observability:Traces:ExcludeSources:0", noisy)),
            builder => builder.AddSource(noisy, useful));

        Sources.Span(noisySource, "span-noisy");
        Sources.Span(usefulSource, "span-useful");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/traces", "span-useful"), Is.True);
            Assert.That(receiver.Received("/v1/traces", "span-noisy"), Is.False);
        });
    }

    [Test]
    public async Task Headers_are_sent_with_every_export()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.headers");
        using var source = new ActivitySource(sourceName);

        await using var running = await Running.Start(
            Configured(receiver, ("Observability:Otlp:Headers", "x-api-key=secret-value-1")),
            builder => builder.AddSource(sourceName));

        Sources.Span(source, "span-headers");
        running.Flush();

        Assert.That(receiver.To("/v1/traces").Select(request => request.Headers["x-api-key"]), Has.All.EqualTo("secret-value-1"));
    }

    [Test]
    public async Task Headers_can_come_from_a_file()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.file");
        using var source = new ActivitySource(sourceName);
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "x-api-key=from-file\n");

        try
        {
            await using var running = await Running.Start(
                Configured(receiver, ("Observability:Otlp:HeadersFile", path)),
                builder => builder.AddSource(sourceName));

            Sources.Span(source, "span-file");
            running.Flush();

            Assert.That(receiver.To("/v1/traces").Select(request => request.Headers["x-api-key"]), Has.All.EqualTo("from-file"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task The_standard_variables_configure_the_exporter_when_the_section_does_not()
    {
        using var receiver = new OtlpReceiver();
        var sourceName = Names.Unique("export.env");
        using var source = new ActivitySource(sourceName);

        var variables = new Dictionary<string, string>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = receiver.Endpoint.ToString(),
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "x-api-key=from-variable",
            ["OTEL_EXPORTER_OTLP_TIMEOUT"] = "5000",
        };

        await using var running = await Running.Start(
            new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "1")),
            builder => builder.AddSource(sourceName),
            services: services => services.AddSingleton(new Internal.Ambient(name => variables.GetValueOrDefault(name), [])));

        Sources.Span(source, "span-env");
        running.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(receiver.Received("/v1/traces", "span-env"), Is.True);
            Assert.That(receiver.To("/v1/traces").Select(request => request.Headers["x-api-key"]), Has.All.EqualTo("from-variable"));
        });
    }
}
