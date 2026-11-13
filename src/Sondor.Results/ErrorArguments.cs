using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Sondor.Results;

/// <summary>
/// The named arguments of an <see cref="Error"/>, and the code of the <see cref="ErrorTemplate"/> they fill. Immutable.
/// </summary>
/// <remarks>
/// A layer that translates or displays an error reads these: the template code is the generic wording to look up, the
/// <see cref="ErrorArgumentKind.Term"/> arguments are the vocabulary to fill it with, and the other kinds are for it to leave
/// out unless it has been told otherwise. <see cref="ToString"/> lists the names, never the values.
/// </remarks>
public sealed class ErrorArguments : IReadOnlyList<ErrorArgument>, IEquatable<ErrorArguments>
{
    private readonly ErrorArgument[] _items;

    internal ErrorArguments(ErrorArgument[] items, string? template, string? description)
    {
        _items = items;
        Template = template;
        Description = description;
    }

    /// <summary>No arguments, and no template: what an error made with <c>new Error(...)</c> has.</summary>
    public static ErrorArguments Empty { get; } = new([], null, null);

    /// <summary>
    /// The code of the generic template the error was made from, such as <c>Resource.NotFound</c>, or null when it was not made
    /// from one. When the error's own code differs (<see cref="ErrorTemplate.WithCode"/>), this is the more general code to
    /// fall back to when nothing is known about the specific one.
    /// </summary>
    public string? Template { get; }

    // The description of the error these belong to. Kept here so Error needs only one reference for both (see Error).
    internal string? Description { get; }

    // Nothing to keep: no arguments and no template. An Error stores its plain description instead.
    internal bool IsEmpty => _items.Length == 0 && Template is null;

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public ErrorArgument this[int index] => _items[index];

    internal ErrorArguments WithDescription(string description) => new(_items, Template, description);

    /// <summary>Finds an argument by name (case-sensitive).</summary>
    public bool TryGetValue(string name, out ErrorArgument argument)
    {
        foreach (var item in _items)
        {
            if (string.Equals(item.Name, name, StringComparison.Ordinal))
            {
                argument = item;

                return true;
            }
        }

        argument = default;

        return false;
    }

    /// <inheritdoc />
    public IEnumerator<ErrorArgument> GetEnumerator() => ((IEnumerable<ErrorArgument>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals([NotNullWhen(true)] ErrorArguments? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || !string.Equals(Template, other.Template, StringComparison.Ordinal) || _items.Length != other._items.Length)
        {
            return false;
        }

        for (var i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ErrorArguments other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Template);

        foreach (var item in _items)
        {
            hash.Add(item.Name);
        }

        return hash.ToHashCode();
    }

    /// <summary>The argument names, such as <c>[Entity, Property]</c>. Values are never shown.</summary>
    public override string ToString() => $"[{string.Join(", ", _items.Select(static item => item.Name))}]";
}
