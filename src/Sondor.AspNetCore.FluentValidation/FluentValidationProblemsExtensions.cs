using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sondor.AspNetCore.Internal;

namespace Sondor.AspNetCore;

/// <summary>Adds FluentValidation to the problem responses of <c>Sondor.AspNetCore</c>.</summary>
public static class FluentValidationProblemsExtensions
{
    /// <summary>The <c>errorCode</c> of a validation failure, the same code <c>Sondor.Cqrs.FluentValidation</c> gives a refused request.</summary>
    public const string ErrorCode = "Validation.Failed";

    private static readonly Error ValidationFailed = new(ErrorCode, "One or more validation errors occurred.", ErrorType.Validation);

    /// <summary>
    /// Answers a FluentValidation <see cref="ValidationException"/> with a problem response: status 400 (the status of
    /// <see cref="ErrorType.Validation"/>, which you can change in <c>ErrorResponseOptions</c>), the code <see cref="ErrorCode"/> and an
    /// <c>errors</c> member that lists the messages of each property. Property names follow the JSON naming policy of the API
    /// (camelCase by default), so they match the properties of the request body. Call <c>AddSondorProblemDetails()</c> too.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    /// <remarks>
    /// The messages are your validators' messages and are sent to the caller as they are: FluentValidation's default message for some
    /// rules includes the rejected value, so write your own with <c>WithMessage</c> where that matters. Calling this twice adds
    /// nothing the second time, and a mapping you make yourself for <see cref="ValidationException"/> in
    /// <c>ErrorResponseOptions.Exceptions</c> still wins when it is configured after this call.
    /// </remarks>
    public static IServiceCollection AddSondorFluentValidationProblems(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProblemContributor, ValidationProblemContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<ErrorResponseOptions>, MapValidationException>());

        return services;
    }

    private sealed class MapValidationException : IConfigureOptions<ErrorResponseOptions>
    {
        public void Configure(ErrorResponseOptions options) =>
            options.Exceptions.Map<ValidationException>(static _ => ValidationFailed);
    }
}
