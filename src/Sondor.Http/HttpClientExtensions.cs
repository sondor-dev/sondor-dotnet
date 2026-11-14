using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sondor.Http.Internal;

namespace Sondor.Http;

/// <summary>Registers named clients that return results.</summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Registers a named client. Its <see cref="HttpClient"/> comes from <see cref="IHttpClientFactory"/> and is returned as the
    /// framework's own <see cref="IHttpClientBuilder"/>, so every handler, resilience pipeline and <c>ConfigureHttpClient</c> call
    /// works as usual. Get the client from <see cref="IHttpResultClientFactory"/>.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="name">The name of the client. Calling this again with the same name adds to it.</param>
    /// <param name="configure">Changes the <see cref="HttpResultOptions"/> of this client. Bind from configuration with <c>Configure&lt;HttpResultOptions&gt;(name, section)</c>.</param>
    /// <returns>The builder of the underlying <see cref="HttpClient"/>.</returns>
    /// <remarks>
    /// The options are validated when the host starts. The <see cref="HttpClient.Timeout"/> of the client is set to
    /// <see cref="HttpResultOptions.Timeout"/>, so an <see cref="HttpClient"/> taken straight from the factory has the same limit.
    /// </remarks>
    public static IHttpClientBuilder AddSondorHttpClient(
        this IServiceCollection services,
        string name,
        Action<HttpResultOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HttpResultOptions>, OptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IHttpResultClientFactory, HttpResultClientFactory>();

        var options = services.AddOptions<HttpResultOptions>(name);

        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();

        return services.AddHttpClient(name).ConfigureHttpClient((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptionsMonitor<HttpResultOptions>>().Get(name);

            if (settings.BaseAddress is not null)
            {
                client.BaseAddress = settings.BaseAddress;
            }

            client.Timeout = settings.Timeout;
        });
    }

    /// <summary>
    /// Registers a named client and a class that uses it. <typeparamref name="TClient"/> is created for each request for it, and takes
    /// the <see cref="HttpResultClient"/> as a constructor parameter, next to anything else it needs.
    /// </summary>
    /// <typeparam name="TClient">The class that makes the calls.</typeparam>
    /// <param name="services">The services.</param>
    /// <param name="name">The name of the client.</param>
    /// <param name="configure">Changes the <see cref="HttpResultOptions"/> of this client.</param>
    /// <returns>The builder of the underlying <see cref="HttpClient"/>.</returns>
    public static IHttpClientBuilder AddSondorHttpClient<TClient>(
        this IServiceCollection services,
        string name,
        Action<HttpResultOptions>? configure = null)
        where TClient : class
    {
        var builder = AddSondorHttpClient(services, name, configure);

        services.AddTransient(provider => ActivatorUtilities.CreateInstance<TClient>(
            provider,
            provider.GetRequiredService<IHttpResultClientFactory>().CreateClient(name)));

        return builder;
    }

    /// <summary>
    /// Adds a reader for another error format to a client (see <see cref="IHttpErrorReader"/>). It is created once, with its
    /// dependencies, when the options of the client are first built.
    /// </summary>
    /// <typeparam name="TReader">The reader.</typeparam>
    /// <param name="builder">The builder returned by <c>AddSondorHttpClient</c>.</param>
    /// <returns>The builder.</returns>
    public static IHttpClientBuilder AddErrorReader<TReader>(this IHttpClientBuilder builder)
        where TReader : class, IHttpErrorReader
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<HttpResultOptions>(builder.Name)
            .Configure<IServiceProvider>((options, provider) =>
                options.ErrorReaders.Add(ActivatorUtilities.CreateInstance<TReader>(provider)));

        return builder;
    }

    /// <summary>Adds a reader for another error format to a client (see <see cref="IHttpErrorReader"/>).</summary>
    /// <param name="builder">The builder returned by <c>AddSondorHttpClient</c>.</param>
    /// <param name="reader">The reader, shared by every call of the client.</param>
    /// <returns>The builder.</returns>
    public static IHttpClientBuilder AddErrorReader(this IHttpClientBuilder builder, IHttpErrorReader reader)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(reader);

        builder.Services.AddOptions<HttpResultOptions>(builder.Name).Configure(options => options.ErrorReaders.Add(reader));

        return builder;
    }
}
