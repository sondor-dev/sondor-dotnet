using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>Adds the per-property <c>errors</c> member to the problem of a <see cref="ValidationException"/>.</summary>
internal sealed class ValidationProblemContributor(IOptions<JsonOptions> json) : IProblemContributor
{
    public void Contribute(ProblemContext context)
    {
        if (context.Exception is not ValidationException { Errors: var failures })
        {
            return;
        }

        var policy = json.Value.SerializerOptions.PropertyNamingPolicy;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var group in failures.GroupBy(failure => Name(failure.PropertyName, policy), StringComparer.Ordinal))
        {
            errors[group.Key] = [.. group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal)];
        }

        context.Problem.Extensions["errors"] = errors;
    }

    // "Items[0].UnitPrice" becomes "items[0].unitPrice" under camelCase: each name in the path is converted, indexers are kept.
    internal static string Name(string path, JsonNamingPolicy? policy)
    {
        if (policy is null || path.Length == 0)
        {
            return path;
        }

        var result = new StringBuilder(path.Length);

        foreach (var segment in path.Split('.'))
        {
            if (result.Length > 0)
            {
                result.Append('.');
            }

            var bracket = segment.IndexOf('[', StringComparison.Ordinal);

            if (bracket < 0)
            {
                result.Append(policy.ConvertName(segment));
            }
            else
            {
                result.Append(policy.ConvertName(segment[..bracket])).Append(segment, bracket, segment.Length - bracket);
            }
        }

        return result.ToString();
    }
}
