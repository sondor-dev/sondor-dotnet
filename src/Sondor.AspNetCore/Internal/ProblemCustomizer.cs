using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>
/// The one place that makes every problem response consistent, whoever produced it: a failed <c>Result</c>, an exception, a status code
/// page or the MVC model binder. The framework calls it for each problem it writes.
/// </summary>
internal sealed class ProblemDetailsOptionsSetup(IOptions<ErrorResponseOptions> options) : IPostConfigureOptions<ProblemDetailsOptions>
{
    public void PostConfigure(string? name, ProblemDetailsOptions problemOptions)
    {
        var settings = options.Value;
        var previous = problemOptions.CustomizeProblemDetails;

        // Ours first, so a customization the application wrote can still adjust what ours set.
        problemOptions.CustomizeProblemDetails = context =>
        {
            ProblemCustomizer.Apply(context, settings);
            previous?.Invoke(context);
        };
    }
}

internal static class ProblemCustomizer
{
    private const string FrameworkTypePrefix = "https://tools.ietf.org/html/rfc9110#";
    private const string TraceIdMember = "traceId";

    public static void Apply(ProblemDetailsContext context, ErrorResponseOptions settings)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        if (!HasCode(problem, settings.ErrorCodeMember))
        {
            // The framework made this problem on its own, so there is no Error: give it a code from its status.
            var code = settings.CodeByStatus.TryGetValue(status, out var known)
                ? known
                : status >= StatusCodes.Status500InternalServerError ? "Error.Internal" : "Request.Failed";

            problem.Extensions[settings.ErrorCodeMember] = code;

            if (settings.TypeUriTemplate is not null && IsFrameworkType(problem.Type))
            {
                problem.Type = Expand(settings.TypeUriTemplate, code);
            }
        }

        var activity = Activity.Current;

        if (settings.BareTraceId && activity is not null)
        {
            problem.Extensions[TraceIdMember] = activity.TraceId.ToString();
        }

        if (settings.TagActivity && activity is not null)
        {
            activity.SetTag("error.type", problem.Extensions[settings.ErrorCodeMember]);

            if (status >= StatusCodes.Status500InternalServerError)
            {
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }
    }

    public static string Expand(string template, string code) =>
        template.Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal);

    private static bool HasCode(ProblemDetails problem, string member) =>
        problem.Extensions.TryGetValue(member, out var value) && value is string { Length: > 0 };

    private static bool IsFrameworkType(string? type) =>
        type is null || type.StartsWith(FrameworkTypePrefix, StringComparison.Ordinal);
}
