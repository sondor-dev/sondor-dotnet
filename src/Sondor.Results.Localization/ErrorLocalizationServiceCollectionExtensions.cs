using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sondor.Results.Localization.Internal;

namespace Sondor.Results.Localization;

/// <summary>Registers <see cref="IErrorLocalizer"/>.</summary>
public static class ErrorLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IErrorLocalizer"/> as a singleton. The translations come from the <see cref="IStringLocalizerFactory"/> of
    /// the application, so register one first: <c>AddLocalization()</c> for <c>.resx</c> files, or your own for JSON or a database.
    /// Add the resources to read with <see cref="ErrorLocalizationOptions.UseResource{TResource}"/>.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Changes the <see cref="ErrorLocalizationOptions"/>.</param>
    /// <returns>The services.</returns>
    /// <remarks>
    /// The options are validated when the host starts: a resource was added but no <see cref="IStringLocalizerFactory"/> is registered,
    /// or an unusable key prefix, source culture or log level. Calling this twice keeps one localizer and both configurations.
    /// </remarks>
    public static IServiceCollection AddErrorLocalization(
        this IServiceCollection services,
        Action<ErrorLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddOptions<ErrorLocalizationOptions>();

        if (configure is not null)
        {
            builder.Configure(configure);
        }

        builder.ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ErrorLocalizationOptions>, OptionsValidator>());

        services.TryAddSingleton<IErrorLocalizer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<ErrorLocalizationOptions>>().Value;

            return new ErrorLocalizer(
                options,
                OptionsValidator.FactoryFor(options, provider),
                provider.GetService<ILoggerFactory>()?.CreateLogger("Sondor.Results.Localization") ?? NullLogger.Instance);
        });

        return services;
    }
}
