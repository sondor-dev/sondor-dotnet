using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Sondor.Observability.Tests;

/// <summary>
/// The whole path against a real OpenTelemetry Collector in a container: the service exports over gRPC and over HTTP, and the
/// collector writes what it received to files that the test reads back. Skipped when no container runtime is reachable.
/// </summary>
[NonParallelizable]
public class CollectorTests
{
    private const string Image = "otel/opentelemetry-collector-contrib:0.165.0";

    private const string Config = """
        receivers:
          otlp:
            protocols:
              grpc:
                endpoint: 0.0.0.0:4317
              http:
                endpoint: 0.0.0.0:4318
        exporters:
          debug:
            verbosity: detailed
        service:
          pipelines:
            traces:
              receivers: [otlp]
              exporters: [debug]
            metrics:
              receivers: [otlp]
              exporters: [debug]
            logs:
              receivers: [otlp]
              exporters: [debug]
        """;

    private IContainer? _collector;

    [OneTimeSetUp]
    public async Task StartCollector()
    {
        try
        {
            _collector = new ContainerBuilder(Image)
                .WithPortBinding(4317, true)
                .WithPortBinding(4318, true)
                .WithResourceMapping(Encoding.UTF8.GetBytes(Config), "/etc/otelcol-contrib/config.yaml")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready"))
                .Build();

            await _collector.StartAsync();
        }
        catch (Exception exception) when (exception is DockerUnavailableException or TimeoutException or HttpRequestException)
        {
            Assert.Ignore($"No container runtime is reachable: {exception.GetType().Name}.");
        }
    }

    [OneTimeTearDown]
    public async Task StopCollector()
    {
        if (_collector is not null)
        {
            await _collector.DisposeAsync();
        }
    }

    [TestCase("Grpc", 4317)]
    [TestCase("HttpProtobuf", 4318)]
    public async Task A_service_exports_all_three_signals_to_a_real_collector(string protocol, int port)
    {
        var name = Names.Unique("collector");
        var sourceName = Names.Unique("collector.source");
        var meterName = Names.Unique("collector.meter");
        using var source = new ActivitySource(sourceName);
        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("collector.things");
        var endpoint = $"http://{_collector!.Hostname}:{_collector.GetMappedPublicPort(port)}";

        await using (var running = await Running.Start(
            new ReloadableConfiguration().With(
                ("Observability:Otlp:Endpoint", endpoint),
                ("Observability:Otlp:Protocol", protocol),
                ("Observability:Traces:SamplingRatio", "1"),
                ("Observability:Version", "4.5.6"),
                ("Observability:Environment", "Testing"),
                ("Observability:ResourceAttributes:team", "platform")),
            builder => builder.AddSource(sourceName).AddMeter(meterName),
            serviceName: name))
        {
            Sources.Span(source, $"span-{protocol}");
            counter.Add(3);
            running.Error($"log-{protocol}");
            running.Flush();
        }

        var output = await Read($"span-{protocol}", "collector.things", $"log-{protocol}");

        Assert.Multiple(() =>
        {
            // Each signal's batch carries its own copy of the resource, so each attribute shows up once for each of the three.
            Assert.That(Occurrences(output, name), Is.GreaterThanOrEqualTo(3), "the service name, on every signal");
            Assert.That(Occurrences(output, "4.5.6"), Is.GreaterThanOrEqualTo(3), "the version");
            Assert.That(Occurrences(output, "Testing"), Is.GreaterThanOrEqualTo(3), "the environment");
            Assert.That(Occurrences(output, "platform"), Is.GreaterThanOrEqualTo(3), "an extra attribute");
        });
    }

    private static int Occurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    /// <summary>Waits for the collector to log everything it was sent, and returns what it logged.</summary>
    private async Task<string> Read(params string[] expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var output = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            var (stdout, stderr) = await _collector!.GetLogsAsync();
            output = stdout + stderr;

            if (expected.All(text => output.Contains(text, StringComparison.Ordinal)))
            {
                return output;
            }

            await Task.Delay(500);
        }

        Assert.Fail($"The collector did not log everything within 30 seconds. Missing: {string.Join(", ", expected.Where(text => !output.Contains(text, StringComparison.Ordinal)))}");

        return output;
    }
}
