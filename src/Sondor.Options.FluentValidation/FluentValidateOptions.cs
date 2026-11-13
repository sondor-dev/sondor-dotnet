using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sondor.Options;

/// <summary>
/// Validates options of one name with the <see cref="IValidator{T}"/> of their type. The validator is resolved from a scope
/// of its own for every validation, so it can depend on scoped services, and the scope is disposed straight after.
/// </summary>
internal sealed class FluentValidateOptions<TOptions>(IServiceProvider services, string? optionsName) : IValidateOptions<TOptions>
    where TOptions : class
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // Options of other names are not this builder's: the framework asks every validator about every name.
        if (optionsName is not null && optionsName != name)
        {
            return ValidateOptionsResult.Skip;
        }

        using var scope = services.CreateScope();

        var validator = scope.ServiceProvider.GetService<IValidator<TOptions>>()
            ?? throw new InvalidOperationException(
                $"There is no IValidator<{typeof(TOptions).Name}> in the container. Register one, or use ValidateFluently<{typeof(TOptions).Name}, TValidator>().");

        var result = validator.Validate(options);

        if (result.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(result.Errors.Select(Describe));
    }

    private static string Describe(FluentValidation.Results.ValidationFailure failure) =>
        string.IsNullOrEmpty(failure.PropertyName)
            ? $"Validation failed for {typeof(TOptions).Name} with the error: {failure.ErrorMessage}"
            : $"Validation failed for {typeof(TOptions).Name}.{failure.PropertyName} with the error: {failure.ErrorMessage}";
}
