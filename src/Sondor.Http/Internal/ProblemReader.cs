using System.Text;
using System.Text.Json;

namespace Sondor.Http.Internal;

/// <summary>
/// Reads the RFC 9457 problem document a failed response may carry: the members <c>Sondor.AspNetCore</c> writes (<c>errorCode</c>,
/// <c>traceId</c>, <c>errors</c>) and the standard ones. Everything is bounded, and nothing in it is trusted.
/// </summary>
internal static class ProblemReader
{
    private const int MaxCodeLength = 128;
    private const int MaxTextLength = 2048;
    private const int MaxTraceIdLength = 128;
    private const int MaxProperties = 100;
    private const int MaxMessages = 20;
    private const int MaxMessageLength = 512;

    public static async ValueTask<Error> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken)
    {
        var options = context.Options;
        var document = await context.ReadJsonAsync(cancellationToken).ConfigureAwait(false);

        RemoteProblem? problem = null;
        string? traceId = null;

        if (document is { RootElement.ValueKind: JsonValueKind.Object } found)
        {
            (problem, traceId) = Parse(found.RootElement, options.ErrorCodeMember);
        }

        var code = problem?.ErrorCode is { } sent && IsValidCode(sent) ? sent : options.CodeFor(context.Status);

        var description = options.UseRemoteDetail && Clean(problem?.Detail, options.MaxRemoteDetailLength) is { Length: > 0 } detail
            ? detail
            : ErrorFactory.DescribeStatus(context.Status);

        return context.CreateError(code, description, options.TypeFor(context.Status), problem, traceId);
    }

    /// <summary>A code the package accepts from the other side: 1 to 128 letters, digits, dots, underscores and hyphens.</summary>
    internal static bool IsValidCode(string code)
    {
        if (code.Length is 0 or > MaxCodeLength)
        {
            return false;
        }

        foreach (var character in code)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static (RemoteProblem? Problem, string? TraceId) Parse(JsonElement root, string codeMember)
    {
        string? type = null;
        string? title = null;
        string? detail = null;
        string? instance = null;
        string? errorCode = null;
        string? traceId = null;
        int? status = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? errors = null;
        var found = false;

        foreach (var property in root.EnumerateObject())
        {
            var name = property.Name;
            var value = property.Value;

            if (name.Equals(codeMember, StringComparison.OrdinalIgnoreCase))
            {
                errorCode = Text(value, MaxTextLength) ?? errorCode;
                found |= errorCode is not null;
            }
            else if (name.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                type = Text(value, MaxTextLength);
                found |= type is not null;
            }
            else if (name.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                title = Text(value, MaxTextLength);
                found |= title is not null;
            }
            else if (name.Equals("detail", StringComparison.OrdinalIgnoreCase))
            {
                detail = Text(value, MaxTextLength);
                found |= detail is not null;
            }
            else if (name.Equals("instance", StringComparison.OrdinalIgnoreCase))
            {
                instance = Text(value, MaxTextLength);
                found |= instance is not null;
            }
            else if (name.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                {
                    status = number;
                    found = true;
                }
            }
            else if (name.Equals("traceId", StringComparison.OrdinalIgnoreCase))
            {
                traceId = Text(value, MaxTraceIdLength);
            }
            else if (name.Equals("errors", StringComparison.OrdinalIgnoreCase))
            {
                errors = Errors(value);
                found |= errors is not null;
            }
        }

        return found ? (new RemoteProblem(type, title, status, detail, instance, errorCode, errors), traceId) : (null, traceId);
    }

    private static string? Text(JsonElement value, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();

        return text is { Length: > 0 } ? Truncate(text, maxLength) : null;
    }

    private static Dictionary<string, IReadOnlyList<string>>? Errors(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var property in value.EnumerateObject())
        {
            if (errors.Count == MaxProperties)
            {
                break;
            }

            var messages = new List<string>();

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (messages.Count == MaxMessages)
                    {
                        break;
                    }

                    if (Text(item, MaxMessageLength) is { } message)
                    {
                        messages.Add(message);
                    }
                }
            }
            else if (Text(property.Value, MaxMessageLength) is { } single)
            {
                messages.Add(single);
            }

            errors[Truncate(property.Name, MaxMessageLength)] = messages;
        }

        return errors;
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];

    /// <summary>Trims the other side's text, drops control characters and cuts it, so it can stand as a description.</summary>
    private static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || maxLength <= 0)
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));

        foreach (var character in text)
        {
            if (builder.Length == maxLength)
            {
                builder.Append('…');

                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString().Trim();
    }
}
