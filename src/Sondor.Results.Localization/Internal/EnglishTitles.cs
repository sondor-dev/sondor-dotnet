namespace Sondor.Results.Localization.Internal;

/// <summary>The English title of each kind of failure: the last layer of a title, always there.</summary>
internal static class EnglishTitles
{
    public static string For(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.RateLimited => "Too many requests",
        ErrorType.FeatureDisabled => "Not available",
        ErrorType.InternalServerError => "Unexpected error",
        _ => "Request failed"
    };
}
