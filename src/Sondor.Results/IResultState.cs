using System.Diagnostics.CodeAnalysis;

namespace Sondor.Results;

/// <summary>
/// A read-only view of a <see cref="Result"/> or a <see cref="Result{T}"/>, so cross-cutting code (logging, tracing, a
/// pipeline behaviour) can look at the outcome of any operation without knowing its value type.
/// </summary>
public interface IResultState
{
    /// <summary>Whether the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>The error when the operation failed; <see cref="Results.Error.None"/> otherwise.</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Error is the established name of this member and is only a keyword in Visual Basic, which this library does not target.")]
    Error Error { get; }
}
