using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sondor.AspNetCore.Internal;

/// <summary>Tells a request that wants a problem response from one that wants a page.</summary>
internal static class RequestClassifier
{
    public static bool IsApiRequest(HttpContext context)
    {
        // The exception handler middleware clears the endpoint before it calls a handler and keeps the original in this feature.
        var endpoint = context.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? context.GetEndpoint();

        if (endpoint is not null)
        {
            var metadata = endpoint.Metadata;

            if (metadata.GetMetadata<IApiBehaviorMetadata>() is not null)
            {
                return true;
            }

            // A Razor Page or a controller that is not an API controller renders a page; anything else (a minimal API) is an API.
            return metadata.GetMetadata<ControllerActionDescriptor>() is null && metadata.GetMetadata<PageActionDescriptor>() is null;
        }

        return !PrefersHtml(context.Request);
    }

    private static bool PrefersHtml(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;

        if (accept.Count == 0)
        {
            return false;
        }

        // A browser asks for text/html first and lists */* with a lower quality; a client that lists */* alone does not prefer HTML.
        var preferred = accept.OrderByDescending(value => value.Quality ?? 1.0).First();

        return preferred.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
               preferred.MediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }
}
