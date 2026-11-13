using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Options.Benchmarks;

/// <summary>A configuration source that can change and announce it, as an Azure App Configuration refresh does.</summary>
public sealed class ReloadableConfiguration : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Set(params (string Key, string? Value)[] values)
    {
        Data = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        OnReload();
    }
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int Timeout { get; set; } = 30;

    public int Retries { get; set; } = 3;

    public bool Pooling { get; set; } = true;

    public string Schema { get; set; } = "public";
}

public static class Containers
{
    public static IConfiguration Configuration(ReloadableConfiguration source) => new ConfigurationBuilder().Add(source).Build();

    public static ServiceProvider Build(IConfiguration configuration, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        register(services);

        return services.BuildServiceProvider();
    }
}
