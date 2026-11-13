using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Sondor.Observability.Tests;

/// <summary>The identity every signal reports: name, version, environment, instance and the extra attributes.</summary>
public class ResourceTests
{
    private static Dictionary<string, object> Attributes(BaseProvider provider) =>
        provider.GetResource().Attributes.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Test]
    public async Task All_three_signals_share_one_resource_with_one_instance_id()
    {
        await using var running = await Running.Start(new ReloadableConfiguration().With());

        var traces = Attributes(running.Tracer);
        var metrics = Attributes(running.Meter);
        var logs = Attributes(running.Logger);

        Assert.Multiple(() =>
        {
            Assert.That(traces["service.name"], Is.EqualTo("test-service"));
            Assert.That(traces["service.instance.id"], Is.Not.Null.And.Not.Empty);
            Assert.That(metrics["service.instance.id"], Is.EqualTo(traces["service.instance.id"]));
            Assert.That(logs["service.instance.id"], Is.EqualTo(traces["service.instance.id"]));
            Assert.That(metrics["service.name"], Is.EqualTo("test-service"));
            Assert.That(logs["service.name"], Is.EqualTo("test-service"));
        });
    }

    [Test]
    public async Task Configuration_sets_version_environment_instance_and_extra_attributes()
    {
        await using var running = await Running.Start(new ReloadableConfiguration().With(
            ("Observability:Version", "1.2.3"),
            ("Observability:Environment", "Staging"),
            ("Observability:InstanceId", "instance-7"),
            ("Observability:ResourceAttributes:team", "payments")));

        var attributes = Attributes(running.Tracer);

        Assert.Multiple(() =>
        {
            Assert.That(attributes["service.version"], Is.EqualTo("1.2.3"));
            Assert.That(attributes["deployment.environment.name"], Is.EqualTo("Staging"));
            Assert.That(attributes["service.instance.id"], Is.EqualTo("instance-7"));
            Assert.That(attributes["team"], Is.EqualTo("payments"));
        });
    }

    [Test]
    public async Task The_builder_wins_over_configuration()
    {
        await using var running = await Running.Start(
            new ReloadableConfiguration().With(
                ("Observability:Version", "1.0.0"),
                ("Observability:Environment", "Staging"),
                ("Observability:InstanceId", "from-configuration"),
                ("Observability:ResourceAttributes:team", "from-configuration")),
            builder => builder
                .WithVersion("9.9.9")
                .WithEnvironment("Production")
                .WithInstanceId("from-code")
                .WithResourceAttribute("team", "from-code"));

        var attributes = Attributes(running.Meter);

        Assert.Multiple(() =>
        {
            Assert.That(attributes["service.version"], Is.EqualTo("9.9.9"));
            Assert.That(attributes["deployment.environment.name"], Is.EqualTo("Production"));
            Assert.That(attributes["service.instance.id"], Is.EqualTo("from-code"));
            Assert.That(attributes["team"], Is.EqualTo("from-code"));
        });
    }

    [Test]
    public async Task The_environment_defaults_to_the_hosts_and_the_instance_to_a_guid()
    {
        await using var running = await Running.Start(new ReloadableConfiguration().With());

        var attributes = Attributes(running.Tracer);

        Assert.Multiple(() =>
        {
            // The empty host this test builds has the framework's default environment name.
            Assert.That(attributes["deployment.environment.name"], Is.EqualTo("Production"));
            Assert.That(Guid.TryParse((string)attributes["service.instance.id"], out _), Is.True);
        });
    }

    [Test]
    public async Task Nothing_in_the_resource_is_named_after_this_package()
    {
        await using var running = await Running.Start(new ReloadableConfiguration().With(), serviceName: "billing");

        var attributes = Attributes(running.Tracer);

        Assert.That(attributes.Keys.Concat(attributes.Values.OfType<string>()), Has.None.Contains("Sondor"));
    }
}
