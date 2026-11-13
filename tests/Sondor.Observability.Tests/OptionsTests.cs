using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sondor.Observability.Internal;

namespace Sondor.Observability.Tests;

/// <summary>Validation at startup, the standard variables and secret files.</summary>
public class OptionsTests
{
    private static readonly Ambient Nothing = new(_ => null, []);

    private static IReadOnlyList<string> Problems(ObservabilityOptions options, Ambient? ambient = null)
    {
        var result = new OptionsValidator(ambient ?? Nothing).Validate(null, options);

        return result.Failed ? [.. result.Failures] : [];
    }

    [Test]
    public void The_defaults_are_valid()
    {
        Assert.That(Problems(new ObservabilityOptions()), Is.Empty);
    }

    [TestCase(-0.1)]
    [TestCase(1.5)]
    [TestCase(double.NaN)]
    public void A_sampling_ratio_outside_zero_to_one_is_invalid(double ratio)
    {
        Assert.That(Problems(new ObservabilityOptions { Traces = new TracesOptions { SamplingRatio = ratio } }), Has.Some.Contains("SamplingRatio"));
    }

    [Test]
    public void Metrics_cannot_be_errors_only()
    {
        Assert.That(Problems(new ObservabilityOptions { Metrics = new SignalOptions { Mode = SignalMode.ErrorsOnly } }), Has.Some.Contains("Metrics cannot be ErrorsOnly"));
    }

    [Test]
    public void An_unknown_mode_is_invalid()
    {
        Assert.That(Problems(new ObservabilityOptions { Logs = new SignalOptions { Mode = (SignalMode)42 } }), Has.Some.Contains("signal mode"));
    }

    [Test]
    public void Empty_names_are_invalid()
    {
        var options = new ObservabilityOptions
        {
            Traces = new TracesOptions { ExcludeSources = [" "] },
            ResourceAttributes = new Dictionary<string, string> { [""] = "x" },
        };

        Assert.That(Problems(options), Has.Some.Contains("ExcludeSources").And.Some.Contains("ResourceAttributes"));
    }

    [TestCase("not a url")]
    [TestCase("ftp://collector:4317")]
    public void An_endpoint_that_is_not_http_is_invalid(string endpoint)
    {
        Assert.That(Problems(new ObservabilityOptions { Otlp = new OtlpOptions { Endpoint = endpoint } }), Has.Some.Contains("endpoint"));
    }

    [TestCase("no-equals-sign")]
    [TestCase("=value-without-key")]
    public void Malformed_headers_are_invalid_and_the_message_does_not_echo_them(string headers)
    {
        var problems = Problems(new ObservabilityOptions { Otlp = new OtlpOptions { Endpoint = "http://c:4317", Headers = headers } });

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains("headers"));
            Assert.That(problems, Has.None.Contains(headers));
        });
    }

    [Test]
    public void Headers_and_a_headers_file_together_are_invalid()
    {
        var path = Path.GetTempFileName();

        try
        {
            var problems = Problems(new ObservabilityOptions { Otlp = new OtlpOptions { Headers = "a=b", HeadersFile = path } });

            Assert.That(problems, Has.Some.Contains("not both"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void A_missing_headers_file_is_invalid()
    {
        var problems = Problems(new ObservabilityOptions { Otlp = new OtlpOptions { HeadersFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) } });

        Assert.That(problems, Has.Some.Contains("headers file"));
    }

    [Test]
    public void A_timeout_of_zero_is_invalid()
    {
        Assert.That(Problems(new ObservabilityOptions { Otlp = new OtlpOptions { Timeout = TimeSpan.Zero } }), Has.Some.Contains("timeout"));
    }

    [Test]
    public void An_unknown_protocol_is_invalid()
    {
        Assert.That(Problems(new ObservabilityOptions { Otlp = new OtlpOptions { Protocol = (OtlpProtocol)9 } }), Has.Some.Contains("protocol"));
    }

    [TestCase("OTEL_EXPORTER_OTLP_ENDPOINT", "nonsense", "endpoint")]
    [TestCase("OTEL_EXPORTER_OTLP_PROTOCOL", "carrier-pigeon", "OTEL_EXPORTER_OTLP_PROTOCOL")]
    [TestCase("OTEL_EXPORTER_OTLP_TIMEOUT", "soon", "OTEL_EXPORTER_OTLP_TIMEOUT")]
    [TestCase("OTEL_EXPORTER_OTLP_HEADERS", "broken", "headers")]
    public void A_bad_standard_variable_is_reported(string variable, string value, string expected)
    {
        var ambient = new Ambient(name => name == variable ? value : null, []);

        Assert.That(Problems(new ObservabilityOptions(), ambient), Has.Some.Contains(expected));
    }

    [Test]
    public void The_section_beats_the_standard_variables()
    {
        var ambient = new Ambient(
            name => name switch
            {
                "OTEL_EXPORTER_OTLP_ENDPOINT" => "http://from-variable:4317",
                "OTEL_EXPORTER_OTLP_PROTOCOL" => "http/protobuf",
                _ => null,
            },
            []);

        var settings = OtlpSettings.Resolve(new OtlpOptions { Endpoint = "http://from-section:4317", Protocol = OtlpProtocol.Grpc }, ambient, []);

        Assert.Multiple(() =>
        {
            Assert.That(settings!.Endpoint.Host, Is.EqualTo("from-section"));
            Assert.That(settings.Protocol, Is.EqualTo(OtlpProtocol.Grpc));
            Assert.That(settings.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public void Http_adds_the_signal_path_and_grpc_does_not()
    {
        var http = OtlpSettings.Resolve(new OtlpOptions { Endpoint = "http://c:4318/", Protocol = OtlpProtocol.HttpProtobuf }, Nothing, []);
        var grpc = OtlpSettings.Resolve(new OtlpOptions { Endpoint = "http://c:4317" }, Nothing, []);

        Assert.Multiple(() =>
        {
            Assert.That(http!.EndpointFor("traces").AbsoluteUri, Is.EqualTo("http://c:4318/v1/traces"));
            Assert.That(grpc!.EndpointFor("traces").AbsoluteUri, Is.EqualTo("http://c:4317/"));
        });
    }

    [Test]
    public void A_secret_file_under_a_secret_folder_is_read_and_a_path_elsewhere_is_left_alone()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var inside = Path.Combine(folder, "otlp-headers");
        var outside = Path.GetTempFileName();
        File.WriteAllText(inside, "x-api-key=docker-secret\n");
        File.WriteAllText(outside, "x-api-key=elsewhere");

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(Secrets.Resolve(inside, [folder]), Is.EqualTo("x-api-key=docker-secret"));
                Assert.That(Secrets.Resolve(outside, [folder]), Is.EqualTo(outside), "only the secret folders are trusted");
                Assert.That(Secrets.Resolve("x-api-key=plain", [folder]), Is.EqualTo("x-api-key=plain"));
                Assert.That(Secrets.Resolve(Path.Combine(folder, "missing"), [folder]), Does.EndWith("missing"));
                Assert.That(Secrets.Resolve(Path.Combine(folder, "..", Path.GetFileName(outside)), [folder]), Does.Contain(".."), "no way out of the folder");
            });
        }
        finally
        {
            Directory.Delete(folder, true);
            File.Delete(outside);
        }
    }

    [Test]
    public void A_headers_value_that_is_a_secret_path_resolves_through_the_ambient_folders()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var secret = Path.Combine(folder, "headers");
        File.WriteAllText(secret, "x-api-key=docker-secret");

        try
        {
            var settings = OtlpSettings.Resolve(new OtlpOptions { Endpoint = "http://c:4317", Headers = secret }, new Ambient(_ => null, [folder]), []);

            Assert.That(settings!.Headers, Is.EqualTo("x-api-key=docker-secret"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void An_unreadable_file_is_not_a_secret()
    {
        Assert.That(Secrets.ReadFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), Is.Null);
    }

    [Test]
    public async Task The_host_does_not_start_with_bad_settings()
    {
        var configuration = new ReloadableConfiguration().With(("Observability:Traces:SamplingRatio", "3"));
        Exception? caught = null;

        try
        {
            await Running.Start(configuration);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        Assert.That(caught, Is.Not.Null.And.Message.Contains("SamplingRatio"));
    }

    [Test]
    public void A_service_name_is_required()
    {
        var services = new ServiceCollection();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => services.AddSondorObservability(" "));
            Assert.Throws<ArgumentNullException>(() => services.AddSondorObservability(null!));
            Assert.Throws<ArgumentException>(() => services.AddSondorObservability("svc", section: " "));
            Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddSondorObservability("svc"));
        });
    }

    [Test]
    public void The_builder_refuses_empty_values()
    {
        ObservabilityBuilder? captured = null;
        new ServiceCollection().AddSondorObservability("svc", builder => captured = builder);

        Assert.Multiple(() =>
        {
            Assert.That(captured!.ServiceName, Is.EqualTo("svc"));
            Assert.Throws<ArgumentException>(() => captured.WithVersion(""));
            Assert.Throws<ArgumentException>(() => captured.WithEnvironment(" "));
            Assert.Throws<ArgumentException>(() => captured.WithInstanceId(""));
            Assert.Throws<ArgumentException>(() => captured.WithResourceAttribute(" ", "x"));
            Assert.Throws<ArgumentNullException>(() => captured.WithResourceAttribute("k", null!));
            Assert.Throws<ArgumentNullException>(() => captured.AddSource(null!));
            Assert.Throws<ArgumentNullException>(() => captured.AddMeter(null!));
            Assert.Throws<ArgumentNullException>(() => captured.WithTracing(null!));
            Assert.Throws<ArgumentNullException>(() => captured.WithMetrics(null!));
            Assert.Throws<ArgumentNullException>(() => captured.WithLogging(null!));
        });
    }

    [Test]
    public async Task A_custom_section_name_is_honoured()
    {
        var sourceName = Names.Unique("options.section");
        using var source = new System.Diagnostics.ActivitySource(sourceName);

        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(
            new Microsoft.Extensions.Hosting.HostApplicationBuilderSettings { DisableDefaults = true });
        ((Microsoft.Extensions.Configuration.IConfigurationBuilder)builder.Configuration).Add(
            new ReloadableConfiguration().With(("Telemetry:Traces:SamplingRatio", "1")));
        builder.Services.AddSondorObservability("svc", b => b.AddSource(sourceName), section: "Telemetry");

        using var host = builder.Build();
        await host.StartAsync();

        Assert.That(Sources.Span(source, "custom-section"), Is.True);

        await host.StopAsync();
    }
}
