using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>The default <see cref="IErrorProblemFactory"/>. Scoped, because the contributors and the text provider may be.</summary>
internal sealed class ErrorProblemFactory(
    IOptions<ErrorResponseOptions> options,
    IEnumerable<IProblemContributor> contributors,
    IProblemTextProvider? textProvider = null) : IErrorProblemFactory
{
    public ProblemDetails Create(Error failure, HttpContext context, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = options.Value;
        var status = StatusFor(failure, settings);
        var title = ReasonPhrases.GetReasonPhrase(status);
        var detail = failure.Description;

        if (textProvider?.GetText(failure, context) is { } text)
        {
            title = text.Title;
            detail = text.Detail;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = string.IsNullOrEmpty(title) ? "Error" : title,
            Detail = detail,
            Instance = context.Request.Path.Value,
        };

        problem.Extensions[settings.ErrorCodeMember] = failure.Code;

        if (settings.TypeUriTemplate is not null)
        {
            problem.Type = ProblemCustomizer.Expand(settings.TypeUriTemplate, failure.Code);
        }

        var problemContext = new ProblemContext(context, problem, failure, exception);

        foreach (var contributor in contributors)
        {
            contributor.Contribute(problemContext);
        }

        return problem;
    }

    private static int StatusFor(Error error, ErrorResponseOptions settings)
    {
        if (settings.StatusByCode.TryGetValue(error.Code, out var byCode))
        {
            return byCode;
        }

        return settings.StatusByType.TryGetValue(error.Type, out var byType) ? byType : StatusCodes.Status500InternalServerError;
    }
}
