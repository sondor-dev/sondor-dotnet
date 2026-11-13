namespace Sondor.Results;

/// <summary>
/// The built-in templates behind the <see cref="Error"/> factories, one for each kind of failure. The codes and placeholder names
/// are a contract and do not change; the English wording may be improved in a minor release. Use
/// <see cref="ErrorTemplate.WithCode"/> to give one a code of your own.
/// </summary>
public static class ErrorTemplates
{
    /// <summary><c>Resource.NotFound</c>: something that was asked for does not exist.</summary>
    public static ErrorTemplate NotFound { get; } = new(
        "Resource.NotFound", ErrorType.NotFound,
        "No {Entity} was found for the given {Property}.");

    /// <summary><c>Resource.Conflict</c>: the request clashes with what already exists.</summary>
    public static ErrorTemplate Conflict { get; } = new(
        "Resource.Conflict", ErrorType.Conflict,
        "A {Entity} with that {Property} already exists.");

    /// <summary><c>Resource.Invalid</c>: a value is not valid.</summary>
    public static ErrorTemplate Invalid { get; } = new(
        "Resource.Invalid", ErrorType.Validation,
        "The {Property} of the {Entity} is not valid.");

    /// <summary><c>Request.Unauthorized</c>: the caller is not signed in.</summary>
    public static ErrorTemplate Unauthorized { get; } = new(
        "Request.Unauthorized", ErrorType.Unauthorized,
        "You need to sign in to {Action}.");

    /// <summary><c>Request.Forbidden</c>: the caller is signed in but not allowed.</summary>
    public static ErrorTemplate Forbidden { get; } = new(
        "Request.Forbidden", ErrorType.Forbidden,
        "You do not have permission to {Action}.");

    /// <summary><c>Request.RateLimited</c>: too many requests.</summary>
    public static ErrorTemplate RateLimited { get; } = new(
        "Request.RateLimited", ErrorType.RateLimited,
        "Too many requests were made to {Action}. Try again later.");

    /// <summary><c>Feature.Disabled</c>: the feature is switched off.</summary>
    public static ErrorTemplate FeatureDisabled { get; } = new(
        "Feature.Disabled", ErrorType.FeatureDisabled,
        "{Feature} is not available.");

    /// <summary><c>Error.Internal</c>: the operation failed in a way the caller did not cause and cannot fix.</summary>
    public static ErrorTemplate Internal { get; } = new(
        "Error.Internal", ErrorType.InternalServerError,
        "An unexpected error occurred while trying to {Action}.");

    /// <summary><c>Error.Failed</c>: the operation failed in an expected way that fits no other kind.</summary>
    public static ErrorTemplate Failed { get; } = new(
        "Error.Failed", ErrorType.Failure,
        "Could not {Action}.");
}
