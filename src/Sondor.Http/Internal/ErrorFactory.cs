namespace Sondor.Http.Internal;

/// <summary>Makes the errors of this package. Every one carries the <see cref="HttpFailure"/> of its call.</summary>
internal static class ErrorFactory
{
    public const string Unavailable = "Http.Unavailable";
    public const string Timeout = "Http.Timeout";
    public const string TooLarge = "Http.TooLarge";
    public const string InvalidResponse = "Http.InvalidResponse";

    public static Error Create(string code, string description, ErrorType type, HttpFailure failure) =>
        // A template is the one public way to give an error arguments. The description is a literal sentence, so a brace in it (it can
        // come from the other side) is escaped rather than read as a placeholder.
        new ErrorTemplate(code, type, EscapeBraces(description))
            .Create(ErrorArgument.Data(HttpFailure.ArgumentName, failure));

    public static Error NoResponse(string code, string description, HttpFailure failure) =>
        Create(code, description, ErrorType.Failure, failure);

    public static string DescribeStatus(int status) => status switch
    {
        400 or 422 => "The request was not valid.",
        401 => "The request was not authorized.",
        403 => "The request is not allowed.",
        404 => "What was asked for was not found.",
        409 => "The request conflicts with the current state.",
        429 => "Too many requests were made.",
        503 => "The service is not available.",
        504 => "The service did not answer in time.",
        >= 500 => "The service failed to complete the request.",
        _ => "The request failed."
    };

    public static string Describe(string code) => code switch
    {
        Unavailable => "The service could not be reached.",
        Timeout => "The call took longer than allowed.",
        TooLarge => "The response was larger than allowed.",
        _ => "The response was not what was expected."
    };

    private static string EscapeBraces(string text) =>
        text.Contains('{', StringComparison.Ordinal) || text.Contains('}', StringComparison.Ordinal)
            ? text.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)
            : text;
}
