namespace Sondor.Results;

/// <summary>
/// One named piece of context in an <see cref="Error"/>, the value behind a <c>{Placeholder}</c> of an
/// <see cref="ErrorTemplate"/>. Make one with <see cref="Term"/>, <see cref="Data"/> or <see cref="Sensitive"/>.
/// </summary>
/// <remarks>
/// <c>default(ErrorArgument)</c> has no name and is rejected wherever an argument is used.
/// <see cref="ToString"/> shows the name and the kind, never the value.
/// </remarks>
public readonly struct ErrorArgument : IEquatable<ErrorArgument>
{
    /// <summary>The text a template shows in place of a <see cref="ErrorArgumentKind.Sensitive"/> argument.</summary>
    public const string RedactedText = "[redacted]";

    private ErrorArgument(string name, object? value, ErrorArgumentKind kind)
    {
        Name = name;
        Value = value;
        Kind = kind;
    }

    /// <summary>The placeholder this argument fills, for example <c>Entity</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// The value: the text of a <see cref="ErrorArgumentKind.Term"/>, the value of a <see cref="ErrorArgumentKind.Data"/>, and
    /// always <see langword="null"/> for a <see cref="ErrorArgumentKind.Sensitive"/> argument.
    /// </summary>
    public object? Value { get; }

    /// <summary>What the argument is, and so whether it may be shown.</summary>
    public ErrorArgumentKind Kind { get; }

    /// <summary>Whether this is a <see cref="ErrorArgumentKind.Sensitive"/> argument, whose value was never kept.</summary>
    public bool IsSensitive => Kind == ErrorArgumentKind.Sensitive;

    /// <summary>A name the developer chose (an entity, a property, an action): safe to show and to translate.</summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="value"/> is null or blank.</exception>
    public static ErrorArgument Term(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new ErrorArgument(name, value, ErrorArgumentKind.Term);
    }

    /// <summary>
    /// A value that may be user-supplied. It travels with the error (for logging, for a layer that chooses to show it) but is
    /// not put in the description unless the template was declared to show data.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    public static ErrorArgument Data(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ErrorArgument(name, value, ErrorArgumentKind.Data);
    }

    /// <summary>
    /// Records that a value exists without keeping it: personal data, a secret. Pass the name only; there is no way to attach
    /// the value, so it cannot leak from the error later.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    public static ErrorArgument Sensitive(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new ErrorArgument(name, null, ErrorArgumentKind.Sensitive);
    }

    /// <inheritdoc />
    public bool Equals(ErrorArgument other) =>
        string.Equals(Name, other.Name, StringComparison.Ordinal) && Kind == other.Kind && Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ErrorArgument other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Kind, Value);

    /// <summary>Whether two arguments have the same name, kind and value.</summary>
    public static bool operator ==(ErrorArgument left, ErrorArgument right) => left.Equals(right);

    /// <summary>Whether two arguments differ.</summary>
    public static bool operator !=(ErrorArgument left, ErrorArgument right) => !left.Equals(right);

    /// <summary>The name and the kind, such as <c>Entity (Term)</c>. The value is left out so logging an argument cannot leak it.</summary>
    public override string ToString() => $"{Name} ({Kind})";
}
