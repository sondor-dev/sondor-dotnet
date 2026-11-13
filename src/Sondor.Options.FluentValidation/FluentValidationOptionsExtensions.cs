using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Sondor.Options;

/// <summary>Validates options with FluentValidation.</summary>
public static class FluentValidationOptionsExtensions
{
    /// <summary>
    /// Validates the options with the <see cref="IValidator{T}"/> registered for <typeparamref name="TOptions"/>, when the host
    /// starts and each time the configuration reloads.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="builder">The options builder, from <c>AddSondorOptions</c> or the framework's <c>AddOptions</c>.</param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// Register the validator yourself, or use <see cref="ValidateFluently{TOptions, TValidator}"/>. A failure is reported as
    /// <c>Validation failed for Options.Property with the error: message</c>, one for each failing rule. Only synchronous rules
    /// can be used: FluentValidation throws when an asynchronous rule is run synchronously, which fails the start. With an
    /// optional section that is not configured, the validator does not run.
    /// </remarks>
    public static OptionsBuilder<TOptions> ValidateFluently<TOptions>(this OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddSingleton<IValidateOptions<TOptions>>(
            services => new FluentValidateOptions<TOptions>(services, builder.Name));

        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TValidator"/> as the validator of <typeparamref name="TOptions"/> (scoped, unless one is
    /// already registered) and validates the options with it, when the host starts and each time the configuration reloads.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <typeparam name="TValidator">The validator. It can depend on scoped services.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <returns>The builder.</returns>
    public static OptionsBuilder<TOptions> ValidateFluently<TOptions, TValidator>(this OptionsBuilder<TOptions> builder)
        where TOptions : class
        where TValidator : class, IValidator<TOptions>
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddScoped<IValidator<TOptions>, TValidator>();

        return builder.ValidateFluently();
    }
}
