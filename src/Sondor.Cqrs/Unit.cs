namespace Sondor.Cqrs;

/// <summary>The response of a request that has nothing to return, so every request has a response type.</summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The only value.</summary>
    public static Unit Value => default;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public override string ToString() => "()";

    /// <summary>All units are equal.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>All units are equal.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
