using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FrameworkOptions = Microsoft.Extensions.Options.Options;

namespace Sondor.Options;

/// <summary>
/// Builds the options the way the framework does, except that the section decides whether validation runs (a required
/// section that is missing fails with a message naming it, an optional one that is missing is built without validation) and
/// that a reload into invalid options can keep the last valid ones.
/// </summary>
internal sealed partial class SectionOptionsFactory<TOptions>(
    ISondorOptionsState<TOptions> state,
    string path,
    SectionRequirement requirement,
    InvalidReloadBehavior invalidReload,
    ILogger logger,
    IEnumerable<IConfigureOptions<TOptions>> setups,
    IEnumerable<IPostConfigureOptions<TOptions>> postConfigures,
    IEnumerable<IValidateOptions<TOptions>> validations) : IOptionsFactory<TOptions>
    where TOptions : class
{
    private readonly OptionsFactory<TOptions> _validated = new(setups, postConfigures, validations);

    private readonly OptionsFactory<TOptions> _unvalidated = new(setups, postConfigures, []);

    private volatile TOptions? _lastValid;

    public TOptions Create(string name)
    {
        // Named options of the same type are not bound to this section: they behave as the framework's do.
        if (name != FrameworkOptions.DefaultName)
        {
            return _validated.Create(name);
        }

        try
        {
            var options = CreateForSection(name);
            _lastValid = options;

            return options;
        }
        catch (Exception exception) when (invalidReload == InvalidReloadBehavior.KeepLastValid
                                          && _lastValid is not null
                                          && exception is not OutOfMemoryException)
        {
            // Only a reload gets here: the first build (at startup) has nothing to keep and fails.
            RejectedReload(logger, typeof(TOptions).Name, path, exception.Message);

            return _lastValid;
        }
    }

    private TOptions CreateForSection(string name)
    {
        if (state.IsConfigured)
        {
            return _validated.Create(name);
        }

        if (requirement == SectionRequirement.Required)
        {
            throw new OptionsValidationException(
                name,
                typeof(TOptions),
                [$"The configuration section '{path}' is required for {typeof(TOptions).Name} but it is missing."]);
        }

        return _unvalidated.Create(name);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "The configuration change for {OptionsType} (section '{Section}') was rejected and the last valid options stay in use: {Reason}")]
    private static partial void RejectedReload(ILogger logger, string optionsType, string section, string reason);
}
