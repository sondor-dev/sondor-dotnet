using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>
/// Adds the per-property <c>errors</c> member to the problem of a <see cref="ValidationException"/>, or of a failed <c>Result</c> whose
/// error carries the messages of each property.
/// </summary>
internal sealed class ValidationProblemContributor(IOptions<JsonOptions> json) : IProblemContributor
{
    // The name of the Data argument that Sondor.Cqrs.Results puts on the error of a refused request (CqrsResultArguments.Errors). The
    // spelling is the contract, so this package needs no reference to that one.
    private const string ErrorsArgument = "Errors";

    public void Contribute(ProblemContext context)
    {
        var policy = json.Value.SerializerOptions.PropertyNamingPolicy;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (context.Exception is ValidationException { Errors: var failures })
        {
            foreach (var group in failures.GroupBy(failure => Name(failure.PropertyName, policy), StringComparer.Ordinal))
            {
                errors[group.Key] = [.. group.Select(failure => failure.ErrorMessage).Distinct(StringComparer.Ordinal)];
            }
        }
        else if (context.Error.Arguments.TryGetValue(ErrorsArgument, out var argument) &&
                 argument is { Kind: ErrorArgumentKind.Data, Value: IReadOnlyDictionary<string, string[]> byProperty })
        {
            foreach (var (property, messages) in byProperty)
            {
                var name = Name(property, policy);

                errors[name] = errors.TryGetValue(name, out var existing) ? [.. existing.Union(messages, StringComparer.Ordinal)] : messages;
            }
        }
        else
        {
            return;
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
