using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Results.Localization.Internal;

/// <summary>Checks the options when the host starts, so a mistake is found then and not by the first error that needs translating.</summary>
internal sealed class OptionsValidator(IServiceProviderIsService serviceProviderIsService) : IValidateOptions<ErrorLocalizationOptions>
{
    private const string NoFactory =
        "A resource was added to the error localization, but no IStringLocalizerFactory is registered. " +
        "Call AddLocalization() (for .resx files), or register your own IStringLocalizerFactory, before the errors are translated.";

    /// <summary>The factory the application registered, when a resource needs one.</summary>
    public static IStringLocalizerFactory? FactoryFor(ErrorLocalizationOptions options, IServiceProvider provider)
    {
        if (options.Sources.Count == 0)
        {
            return null;
        }

        // The options are validated when they are read, so a missing factory has already been reported, with the reason.
        return provider.GetRequiredService<IStringLocalizerFactory>();
    }

    public ValidateOptionsResult Validate(string? name, ErrorLocalizationOptions options)
    {
        var failures = new List<string>();

        if (options.KeyPrefix is null)
        {
            failures.Add("KeyPrefix cannot be null; use an empty string for no prefix.");
        }

        if (options.SourceCulture is not null && string.IsNullOrWhiteSpace(options.SourceCulture))
        {
            failures.Add("SourceCulture cannot be blank; name a culture such as 'en', or use null to look English up like any language.");
        }

        if (!Enum.IsDefined(options.MissingTranslationLevel))
        {
            failures.Add($"MissingTranslationLevel '{options.MissingTranslationLevel}' is not a log level.");
        }

        if (options.ShowDataFor.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("ShowDataFor cannot contain a blank code.");
        }

        if (options.Sources.Count > 0 && !serviceProviderIsService.IsService(typeof(IStringLocalizerFactory)))
        {
            failures.Add(NoFactory);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
