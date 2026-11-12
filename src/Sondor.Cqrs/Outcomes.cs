namespace Sondor.Cqrs;

/// <summary>What a response says about the outcome of its request.</summary>
/// <param name="IsSuccess">Whether the request did what it was asked to.</param>
/// <param name="ErrorCode">Why it did not, as a stable code, or null on a success or when the response carries no code.</param>
public readonly record struct RequestOutcome(bool IsSuccess, string? ErrorCode = null)
{
    /// <summary>The outcome of a request that did what it was asked to. Also the outcome of every response nobody can read.</summary>
    public static RequestOutcome Success => new(true);

    /// <summary>The outcome of a request that failed, with the code that says why.</summary>
    /// <param name="errorCode">The code, or null when the response carries none.</param>
    public static RequestOutcome Failure(string? errorCode) => new(false, errorCode);
}

/// <summary>
/// Reads the outcome of a response, so tracing, metrics and logging can report a failure that was returned as a value (a result
/// type) and not thrown. Register one for your response types; without one every response that is returned counts as a success.
/// <c>Sondor.Cqrs.Results</c> registers one for <c>Result</c> and <c>Result&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IOutcomeReader<in TResponse>
{
    /// <summary>Reads the outcome of <paramref name="response"/>.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The outcome.</returns>
    RequestOutcome Read(TResponse response);
}

/// <summary>The reasons a behaviour refuses a request.</summary>
public enum FailureKind
{
    /// <summary>The request could not be done.</summary>
    Failure = 0,

    /// <summary>The request is not valid.</summary>
    Validation = 1,
}

/// <summary>A refusal a behaviour hands to an <see cref="IFailureFactory{TResponse}"/>.</summary>
/// <param name="Code">A stable code, for example <c>Validation.Failed</c>.</param>
/// <param name="Description">What went wrong, for a person to read. Never contains a rejected value.</param>
/// <param name="Kind">The kind of refusal.</param>
public readonly record struct RequestFailure(string Code, string Description, FailureKind Kind);

/// <summary>
/// Builds a response that says "refused", so a behaviour (validation, for one) can stop a request without throwing. Register one
/// for your response types; without one such a behaviour throws instead. <c>Sondor.Cqrs.Results</c> registers one for
/// <c>Result</c> and <c>Result&lt;T&gt;</c>.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IFailureFactory<out TResponse>
{
    /// <summary>Builds the response for a refusal.</summary>
    /// <param name="failure">The refusal.</param>
    /// <returns>A response that carries the failure.</returns>
    TResponse Create(RequestFailure failure);
}
