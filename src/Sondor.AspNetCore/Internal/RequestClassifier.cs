using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// A browser asks for <c>text/html</c> first and lists <c>*/*</c> with a lower quality; a client that lists <c>*/*</c> alone does
    /// not prefer HTML. The header is read in place, without parsing it into objects: an unknown URL is the request a flood of bots
    /// sends, and this runs for every one of them.
    /// </summary>
    private static bool PrefersHtml(HttpRequest request)
    {
        var bestQuality = -1.0;
        var bestIsHtml = false;

        foreach (var header in request.Headers.Accept)
        {
            var remaining = header.AsSpan();

            while (!remaining.IsEmpty)
            {
                var comma = remaining.IndexOf(',');
                var range = comma < 0 ? remaining : remaining[..comma];
                remaining = comma < 0 ? default : remaining[(comma + 1)..];

                ReadRange(range, out var mediaType, out var quality);

                // Strictly greater: the first of equally preferred types wins.
                if (!mediaType.IsEmpty && quality > bestQuality)
                {
                    bestQuality = quality;
                    bestIsHtml = IsHtml(mediaType);
                }
            }
        }

        return bestIsHtml;
    }

    private static void ReadRange(ReadOnlySpan<char> range, out ReadOnlySpan<char> mediaType, out double quality)
    {
        var semicolon = range.IndexOf(';');

        mediaType = (semicolon < 0 ? range : range[..semicolon]).Trim();
        quality = 1.0;

        if (semicolon < 0)
        {
            return;
        }

        var parameters = range[(semicolon + 1)..];

        while (!parameters.IsEmpty)
        {
            var next = parameters.IndexOf(';');
            var parameter = (next < 0 ? parameters : parameters[..next]).Trim();
            parameters = next < 0 ? default : parameters[(next + 1)..];

            if (parameter.Length > 2 && parameter[0] is 'q' or 'Q' && parameter[1] == '=' &&
                double.TryParse(parameter[2..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                quality = parsed;

                return;
            }
        }
    }

    private static bool IsHtml(ReadOnlySpan<char> mediaType) =>
        mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
}
