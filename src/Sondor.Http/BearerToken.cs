using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sondor.Http.Internal;

namespace Sondor.Http;

/// <summary>Supplies the access token a request is sent with. Caching and refreshing it are the provider's job.</summary>
public interface IAccessTokenProvider
{
    /// <summary>Gets a token for a request.</summary>
    /// <param name="request">The request that is about to be sent.</param>
    /// <param name="cancellationToken">Cancelled when the call times out or the caller cancels.</param>
    /// <returns>The token, or null or empty to send the request without one.</returns>
    ValueTask<string?> GetTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>Where a bearer token may be sent.</summary>
public sealed class BearerTokenOptions
{
    /// <summary>
    /// The hosts that may receive the token, compared without regard to case. When it is empty the host of the client's
    /// <see cref="HttpResultOptions.BaseAddress"/> is the only one; with neither, the host does not start. A request to any other host
    /// (an absolute URI in a paging link, say) is sent without the token.
    /// </summary>
    public ISet<string> AllowedHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the token may be sent over plain <c>http</c>. Off by default: only for local development.</summary>
    public bool AllowInsecureHttp { get; set; }
}

/// <summary>Adds an access token to the requests of a named client.</summary>
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "One takes a provider type and the other a delegate, so a call never binds to the wrong one.")]
public static class BearerTokenExtensions
{
    /// <summary>
    /// Sends <c>Authorization: Bearer</c> with every request of the client, with a token from <typeparamref name="TProvider"/>. The
    /// header is set on the request, never on the client's defaults, and only when the host is allowed (see
    /// <see cref="BearerTokenOptions.AllowedHosts"/>), the scheme is <c>https</c> and the request has no <c>Authorization</c> header of
    /// its own. A token is never logged.
    /// </summary>
    /// <typeparam name="TProvider">Supplies the token. One instance serves the client, so it must be safe to use from several threads.</typeparam>
    /// <param name="builder">The builder returned by <c>AddSondorHttpClient</c>.</param>
    /// <param name="configure">Changes the <see cref="BearerTokenOptions"/>.</param>
    /// <returns>The builder.</returns>
    public static IHttpClientBuilder AddSondorBearerToken<TProvider>(
        this IHttpClientBuilder builder,
        Action<BearerTokenOptions>? configure = null)
        where TProvider : class, IAccessTokenProvider
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddKeyedSingleton<IAccessTokenProvider, TProvider>(builder.Name);

        return Add(builder, configure);
    }

    /// <summary>
    /// Sends <c>Authorization: Bearer</c> with every request of the client, with the token the delegate returns. See
    /// <see cref="AddSondorBearerToken{TProvider}"/> for when it is sent.
    /// </summary>
    /// <param name="builder">The builder returned by <c>AddSondorHttpClient</c>.</param>
    /// <param name="getToken">Gets a token for a request. Return null or empty to send it without one.</param>
    /// <param name="configure">Changes the <see cref="BearerTokenOptions"/>.</param>
    /// <returns>The builder.</returns>
    public static IHttpClientBuilder AddSondorBearerToken(
        this IHttpClientBuilder builder,
        Func<HttpRequestMessage, CancellationToken, ValueTask<string?>> getToken,
        Action<BearerTokenOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(getToken);

        builder.Services.AddKeyedSingleton<IAccessTokenProvider>(builder.Name, new DelegateTokenProvider(getToken));

        return Add(builder, configure);
    }

    private static IHttpClientBuilder Add(IHttpClientBuilder builder, Action<BearerTokenOptions>? configure)
    {
        var name = builder.Name;

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BearerTokenOptions>, BearerTokenOptionsValidator>());

        var options = builder.Services.AddOptions<BearerTokenOptions>(name);

        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();

        return builder.AddHttpMessageHandler(provider => new BearerTokenHandler(
            provider.GetRequiredKeyedService<IAccessTokenProvider>(name),
            provider.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(name),
            provider.GetRequiredService<IOptionsMonitor<HttpResultOptions>>().Get(name),
            new Logger<BearerTokenHandler>(provider.GetRequiredService<ILoggerFactory>())));
    }

    private sealed class DelegateTokenProvider(Func<HttpRequestMessage, CancellationToken, ValueTask<string?>> getToken) : IAccessTokenProvider
    {
        public ValueTask<string?> GetTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            getToken(request, cancellationToken);
    }
}

internal sealed class BearerTokenOptionsValidator(IOptionsMonitor<HttpResultOptions> clients) : IValidateOptions<BearerTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, BearerTokenOptions options)
    {
        if (options.AllowedHosts.Count == 0 && clients.Get(name).BaseAddress is null)
        {
            return ValidateOptionsResult.Fail(
                $"The bearer token of the client '{name}' needs a BaseAddress, or at least one host in BearerTokenOptions.AllowedHosts, " +
                "so it is known where the token may be sent.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>Sets the Authorization header of a request, if the request is going where a token is allowed.</summary>
internal sealed class BearerTokenHandler(
    IAccessTokenProvider provider,
    BearerTokenOptions options,
    HttpResultOptions client,
    ILogger<BearerTokenHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null && request.RequestUri is { IsAbsoluteUri: true } uri)
        {
            if (!Allowed(uri, out var reason))
            {
                HttpLog.TokenNotAdded(logger, uri.Authority, reason);
            }
            else if (await provider.GetTokenAsync(request, cancellationToken).ConfigureAwait(false) is { Length: > 0 } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private bool Allowed(Uri uri, out string reason)
    {
        if (uri.Scheme != Uri.UriSchemeHttps && !(options.AllowInsecureHttp && uri.Scheme == Uri.UriSchemeHttp))
        {
            reason = "the scheme is not https";

            return false;
        }

        var allowed = options.AllowedHosts.Count > 0
            ? options.AllowedHosts.Contains(uri.IdnHost)
            : client.BaseAddress is { } baseAddress && string.Equals(baseAddress.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase);

        reason = "the host is not allowed";

        return allowed;
    }
}
