using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;

namespace Sondor.Observability.Internal;

/// <summary>
/// The one resource all three signals share. Worked out once, so the instance id is the same on traces, metrics and logs: the
/// builder first, then configuration, then the defaults.
/// </summary>
internal sealed class ResourceIdentity
{
    private readonly string _serviceName;
    private readonly string? _version;
    private readonly string? _environment;
    private readonly string _instanceId;
    private readonly Dictionary<string, string> _attributes;

    public ResourceIdentity(
        ObservabilityBuilder builder,
        IOptionsMonitor<ObservabilityOptions> options,
        IHostEnvironment? host)
    {
        var configured = options.CurrentValue;

        _serviceName = builder.ServiceName;
        _version = builder.Version ?? Blank(configured.Version) ?? EntryAssemblyVersion();
        _environment = builder.EnvironmentName ?? Blank(configured.Environment) ?? Blank(host?.EnvironmentName);
        _instanceId = builder.InstanceId ?? Blank(configured.InstanceId) ?? Guid.NewGuid().ToString();

        _attributes = new Dictionary<string, string>(configured.ResourceAttributes, StringComparer.Ordinal);

        foreach (var (key, value) in builder.Attributes)
        {
            _attributes[key] = value;
        }
    }

    public void Apply(ResourceBuilder resource)
    {
        resource.AddService(_serviceName, serviceVersion: _version, autoGenerateServiceInstanceId: false, serviceInstanceId: _instanceId);

        if (_environment is not null)
        {
            resource.AddAttributes([new("deployment.environment.name", _environment)]);
        }

        resource.AddAttributes(_attributes.Select(pair => new KeyValuePair<string, object>(pair.Key, pair.Value)));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? EntryAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        // The build metadata after '+' is a commit hash: noise on a version.
        var plus = version.IndexOf('+', StringComparison.Ordinal);

        return plus > 0 ? version[..plus] : version;
    }
}
