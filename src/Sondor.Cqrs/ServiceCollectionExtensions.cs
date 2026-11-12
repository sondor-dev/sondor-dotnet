using Microsoft.Extensions.DependencyInjection;

namespace Sondor.Cqrs;

/// <summary>Registers <c>Sondor.Cqrs</c> with the dependency injection container.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="ISender"/> and <see cref="IPublisher"/>, the handlers of the assemblies named in
    /// <paramref name="configure"/>, and the behaviours it asks for.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configure">Says where the handlers are and which behaviours run.</param>
    /// <returns>The services.</returns>
    /// <exception cref="InvalidOperationException">A request has two handlers, or (unless turned off) none.</exception>
    public static IServiceCollection AddCqrs(this IServiceCollection services, Action<CqrsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CqrsBuilder(services);

        configure(builder);
        builder.Apply();

        return services;
    }
}
