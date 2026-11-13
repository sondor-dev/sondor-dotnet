using Microsoft.Extensions.Options;

namespace Sondor.Http.Internal;

/// <summary>Rejects settings that would only fail later, on the first call: the message names the setting and the value.</summary>
internal sealed class OptionsValidator : IValidateOptions<HttpResultOptions>
{
    public ValidateOptionsResult Validate(string? name, HttpResultOptions options)
    {
        var failures = Check(options);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static List<string> Check(HttpResultOptions options)
    {
        var failures = new List<string>();

        if (options.BaseAddress is { IsAbsoluteUri: false })
        {
            failures.Add($"{nameof(HttpResultOptions.BaseAddress)} must be an absolute URI (it was '{options.BaseAddress}').");
        }

        CheckTimeout(failures, nameof(HttpResultOptions.Timeout), options.Timeout);

        if (options.MaxResponseBytes <= 0)
        {
            failures.Add($"{nameof(HttpResultOptions.MaxResponseBytes)} must be positive (it was {options.MaxResponseBytes}).");
        }

        if (options.MaxErrorBodyBytes < 0)
        {
            failures.Add($"{nameof(HttpResultOptions.MaxErrorBodyBytes)} must not be negative (it was {options.MaxErrorBodyBytes}).");
        }

        if (options.MaxRemoteDetailLength < 0)
        {
            failures.Add($"{nameof(HttpResultOptions.MaxRemoteDetailLength)} must not be negative (it was {options.MaxRemoteDetailLength}).");
        }

        if (options.Json is null)
        {
            failures.Add($"{nameof(HttpResultOptions.Json)} must not be null.");
        }

        if (string.IsNullOrWhiteSpace(options.ErrorCodeMember))
        {
            failures.Add($"{nameof(HttpResultOptions.ErrorCodeMember)} must not be blank.");
        }

        foreach (var (status, code) in options.CodeByStatus)
        {
            CheckStatus(failures, $"{nameof(HttpResultOptions.CodeByStatus)}[{status}]", status);

            if (string.IsNullOrWhiteSpace(code))
            {
                failures.Add($"{nameof(HttpResultOptions.CodeByStatus)}[{status}] must not be a blank code.");
            }
        }

        foreach (var status in options.TypeByStatus.Keys)
        {
            CheckStatus(failures, $"{nameof(HttpResultOptions.TypeByStatus)}[{status}]", status);
        }

        if (options.ErrorReaders.Any(static reader => reader is null))
        {
            failures.Add($"{nameof(HttpResultOptions.ErrorReaders)} must not hold a null reader.");
        }

        return failures;
    }

    internal static void CheckTimeout(List<string> failures, string setting, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            failures.Add($"{setting} must be positive or Timeout.InfiniteTimeSpan (it was {timeout}).");
        }
    }

    private static void CheckStatus(List<string> failures, string setting, int status)
    {
        if (status is < 100 or > 599)
        {
            failures.Add($"{setting} must be a status from 100 to 599.");
        }
    }
}
