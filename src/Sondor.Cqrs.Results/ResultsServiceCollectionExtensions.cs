using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sondor.Cqrs;

/// <summary>Registers <c>Sondor.Results</c> support for <c>Sondor.Cqrs</c>.</summary>
public static class ResultsServiceCollectionExtensions
{
    /// <summary>
    /// Makes the built-in tracing, metrics and logging report a failed <c>Result</c> or <c>Result&lt;T&gt;</c> as a failure with
    /// its error code, and lets a behaviour (validation, for one) refuse a request by returning one. Call it before or after
    /// <c>AddCqrs</c>. A reader or factory you registered yourself for a response type is used instead.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddCqrsResults(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(typeof(IOutcomeReader<>), typeof(ResultOutcomeReader<>));
        services.TryAddSingleton(typeof(IFailureFactory<>), typeof(ResultFailureFactory<>));

        return services;
    }
}
