using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Http;

/// <summary>Creates the <see cref="HttpResultClient"/> of a named client registered with <c>AddSondorHttpClient</c>.</summary>
public interface IHttpResultClientFactory
{
    /// <summary>
    /// Creates a client. It wraps an <see cref="HttpClient"/> from <see cref="IHttpClientFactory"/>, so the handler pipeline of the name
    /// (resilience, tokens, anything you added) applies. It is cheap to create and does not need to be kept.
    /// </summary>
    /// <param name="name">The name given to <c>AddSondorHttpClient</c>.</param>
    /// <returns>The client.</returns>
    HttpResultClient CreateClient(string name);
}

internal sealed class HttpResultClientFactory(
    IHttpClientFactory httpClients,
    IOptionsMonitor<HttpResultOptions> options,
    ILoggerFactory loggers,
    TimeProvider timeProvider) : IHttpResultClientFactory
{
    public HttpResultClient CreateClient(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new HttpResultClient(
            httpClients.CreateClient(name),
            options.Get(name),
            new Logger<HttpResultClient>(loggers),
            timeProvider);
    }
}
