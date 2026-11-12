using System.Diagnostics.CodeAnalysis;

namespace Sondor.Results;

/// <summary>
/// Why an operation failed.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable code in the form <c>Area.Reason</c>, for example <c>Node.NotFound</c>. Callers and clients
/// depend on it, so it does not change once released. It never contains data from the request.
/// </param>
/// <param name="Description">A message for a person, safe to show to the caller of the operation.</param>
/// <param name="Type">The kind of failure.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Error is the established name of this type and is only a keyword in Visual Basic, which this library does not target.")]
public readonly record struct Error(string Code, string Description, ErrorType Type)
{
    /// <summary>The error of a successful result: no code, no description.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>The error of a <see cref="Result{T}"/> that was given a null value.</summary>
    public static readonly Error NullValue = new("Error.NullValue", "The specified result value is null.", ErrorType.Failure);

    /// <summary>
    /// The error of a result that was never created through <see cref="Result.Success()"/> or <see cref="Result.Failure(Error)"/>,
    /// which is what <c>default(Result)</c> is. It is a failure rather than a success so an uninitialised value cannot pass for a good one.
    /// </summary>
    public static readonly Error Unspecified = new(
        "Error.Unspecified",
        "The result was not created as a success or a failure.",
        ErrorType.InternalServerError);

    /// <summary>Whether this is <see cref="None"/>, or has no code.</summary>
    public bool IsNone => string.IsNullOrEmpty(Code);
}
