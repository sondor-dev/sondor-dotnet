using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Sondor.Results;

/// <summary>
/// Why an operation failed.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable code in the form <c>Area.Reason</c>, for example <c>Node.NotFound</c>. Callers and clients
/// depend on it, so it does not change once released. It never contains data from the request.
/// </param>
/// <param name="Description">
/// A message for a person, safe to show to the caller of the operation. It is the English fallback: a layer that translates
/// looks the error up by its code and uses this when it has nothing better. It never contains a value from the request unless
/// its template was declared to show data.
/// </param>
/// <param name="Type">The kind of failure.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Error is the established name of this type and is only a keyword in Visual Basic, which this library does not target.")]
public readonly partial record struct Error(string Code, string Description, ErrorType Type)
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

    // Either the description itself (an error made with the constructor) or the ErrorArguments, which hold the description too
    // (an error made from a template). One reference for both keeps Error at three fields, 24 bytes, as it was before arguments
    // existed: every Result and Result<T> contains an Error, success included, so every extra field would make all of them bigger.
    private readonly object? _detail = Description;

    internal Error(string code, ErrorArguments detail, ErrorType type)
        : this(code, detail.Description!, type)
    {
        _detail = detail;
    }

    /// <summary>
    /// A message for a person, safe to show to the caller of the operation. It is the English fallback: a layer that translates
    /// looks the error up by its code and uses this when it has nothing better.
    /// </summary>
    public string Description
    {
        get => _detail is ErrorArguments arguments ? arguments.Description! : (string?)_detail!;
        init => _detail = _detail is ErrorArguments arguments ? arguments.WithDescription(value) : value;
    }

    /// <summary>
    /// The named pieces of context the description was built from, and the template they fill. Never null: it is
    /// <see cref="ErrorArguments.Empty"/> for an error made with <c>new Error(...)</c>. A translation layer reads this to fill
    /// its own wording; see <see cref="ErrorArgumentKind"/> for which arguments may be shown.
    /// </summary>
    public ErrorArguments Arguments
    {
        get => _detail as ErrorArguments ?? ErrorArguments.Empty;
        init => _detail = value is null || value.IsEmpty ? Description : value.WithDescription(Description);
    }

    /// <summary>Whether this is <see cref="None"/>, or has no code.</summary>
    public bool IsNone => string.IsNullOrEmpty(Code);

    /// <summary>Whether two errors have the same code, description, type and arguments (compared by value).</summary>
    public bool Equals(Error other) =>
        string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(Description, other.Description, StringComparison.Ordinal) &&
        Type == other.Type &&
        Arguments.Equals(other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Code, Description, Type);

    // The generated version would print every public property, which now includes the arguments; this prints their names only,
    // so logging an error cannot leak a value.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = ").Append(Code)
            .Append(", Description = ").Append(Description)
            .Append(", Type = ").Append(Type)
            .Append(", IsNone = ").Append(IsNone);

        if (Arguments.Count > 0)
        {
            builder.Append(", Arguments = ").Append(Arguments);
        }

        return true;
    }
}
