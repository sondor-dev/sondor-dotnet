using Microsoft.Extensions.Options;

namespace Sondor.AspNetCore.Internal;

/// <summary>Rejects settings that would only fail later, on the first error: the message names the setting and the value.</summary>
internal sealed class OptionsValidator : IValidateOptions<ErrorResponseOptions>
{
    public ValidateOptionsResult Validate(string? name, ErrorResponseOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ErrorCodeMember))
        {
            failures.Add($"{nameof(ErrorResponseOptions.ErrorCodeMember)} must not be blank.");
        }

        if (options.TypeUriTemplate is not null && !options.TypeUriTemplate.Contains("{code}", StringComparison.Ordinal))
        {
            failures.Add($"{nameof(ErrorResponseOptions.TypeUriTemplate)} must contain {{code}} (it was '{options.TypeUriTemplate}'), or be null.");
        }

        foreach (var (type, status) in options.StatusByType)
        {
            CheckStatus(failures, $"{nameof(ErrorResponseOptions.StatusByType)}[{type}]", status);
        }

        foreach (var (code, status) in options.StatusByCode)
        {
            CheckStatus(failures, $"{nameof(ErrorResponseOptions.StatusByCode)}['{code}']", status);
        }

        CheckStatus(failures, nameof(ErrorResponseOptions.ClientClosedRequestStatus), options.ClientClosedRequestStatus);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckStatus(List<string> failures, string setting, int status)
    {
        if (status is < 400 or > 599)
        {
            failures.Add($"{setting} must be an error status from 400 to 599 (it was {status}).");
        }
    }
}
