namespace Sondor.Results;

/// <summary>
/// What kind of failure an <see cref="Error"/> is. It says how the failure should be treated, not how it is transported:
/// an ASP.NET Core layer maps it to an HTTP status, a message handler might map it to a retry decision.
/// </summary>
public enum ErrorType
{
    /// <summary>The operation failed in an expected way that fits no other type.</summary>
    Failure,

    /// <summary>The input is not valid.</summary>
    Validation,

    /// <summary>Something that was asked for does not exist.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state, for example a duplicate.</summary>
    Conflict,

    /// <summary>The caller is not authenticated.</summary>
    Unauthorized,

    /// <summary>The caller is authenticated but not allowed.</summary>
    Forbidden,

    /// <summary>The operation failed in a way the caller did not cause and cannot fix.</summary>
    InternalServerError,

    /// <summary>The feature is switched off.</summary>
    FeatureDisabled,

    /// <summary>The caller has made too many requests.</summary>
    RateLimited
}
