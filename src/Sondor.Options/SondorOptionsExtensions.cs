using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sondor.Options;

/// <summary>Registers options bound from a configuration section by convention and validated when the host starts.</summary>
public static class SondorOptionsExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to a configuration section and validates it when the host starts. The section
    /// is named after the type unless <paramref name="section"/> says otherwise, and has to exist unless
    /// <paramref name="requirement"/> says otherwise.
    /// </summary>
    /// <typeparam name="TOptions">The options type. It needs a public parameterless constructor.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="section">The section path, for example <c>Database</c> or <c>Messaging:Broker</c>. Defaults to the type name.</param>
    /// <param name="requirement">Whether the section has to exist. Required unless said otherwise.</param>
    /// <param name="onInvalidReload">
    /// What a reload into invalid options does. By default the last valid options stay in use and a warning is logged, so
    /// a bad change in a configuration store cannot take a running service down. Startup is strict either way.
    /// </param>
    /// <returns>The options builder, to add validation to (<c>ValidateDataAnnotations</c>, <c>Validate</c>, ...).</returns>
    /// <remarks>
    /// <see cref="IOptions{TOptions}"/>, <see cref="IOptionsSnapshot{TOptions}"/> and <see cref="IOptionsMonitor{TOptions}"/>
    /// are all available, and follow a configuration reload. A <see cref="SectionRequirement.Required"/> section that is
    /// missing fails with an <see cref="OptionsValidationException"/> naming it (at startup, or on a reload that removes it).
    /// With <see cref="SectionRequirement.Optional"/>, a missing section is "not configured": no validation runs, the options
    /// are a default instance, and <see cref="ISondorOptionsState{TOptions}.IsConfigured"/> is false. Once the section is
    /// there, it is validated like a required one, so turning a feature on with a bad value still fails. The check is made
    /// each time the options are built. Calling this twice for one type: the last call decides the requirement.
    /// </remarks>
    public static OptionsBuilder<TOptions> AddSondorOptions<TOptions>(
        this IServiceCollection services,
        string? section = null,
        SectionRequirement requirement = SectionRequirement.Required,
        InvalidReloadBehavior onInvalidReload = InvalidReloadBehavior.KeepLastValid)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!Enum.IsDefined(requirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Not a known requirement.");
        }

        if (!Enum.IsDefined(onInvalidReload))
        {
            throw new ArgumentOutOfRangeException(nameof(onInvalidReload), onInvalidReload, "Not a known behaviour.");
        }

        var path = SectionName.For<TOptions>(section);

        services.Replace(ServiceDescriptor.Singleton<ISondorOptionsState<TOptions>>(
            provider => new SectionState<TOptions>(provider.GetRequiredService<IConfiguration>(), path)));

        services.Replace(ServiceDescriptor.Singleton<IOptionsFactory<TOptions>>(
            provider => new SectionOptionsFactory<TOptions>(
                provider.GetRequiredService<ISondorOptionsState<TOptions>>(),
                path,
                requirement,
                onInvalidReload,
                provider.GetService<ILoggerFactory>()?.CreateLogger("Sondor.Options") ?? NullLogger.Instance,
                provider.GetServices<IConfigureOptions<TOptions>>(),
                provider.GetServices<IPostConfigureOptions<TOptions>>(),
                provider.GetServices<IValidateOptions<TOptions>>())));

        return services
            .AddOptions<TOptions>()
            .BindConfiguration(path)
            .ValidateOnStart();
    }

    /// <summary>Tells whether the section that <c>AddSondorOptions</c> binds for <typeparamref name="TOptions"/> has at least one key or value.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="configuration">The configuration.</param>
    /// <param name="section">The section path. Defaults to the type name, as for <c>AddSondorOptions</c>.</param>
    /// <returns>True when the section is configured.</returns>
    /// <remarks>
    /// Use it while registering services, to leave a feature out of the container unless it is configured. It reads the
    /// configuration at that moment: a later reload does not add or remove the feature.
    /// </remarks>
    public static bool HasSondorSection<TOptions>(this IConfiguration configuration, string? section = null)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName.For<TOptions>(section)).Exists();
    }
}
