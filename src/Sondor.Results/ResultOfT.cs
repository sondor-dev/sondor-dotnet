using System.Diagnostics.CodeAnalysis;

namespace Sondor.Results;

/// <summary>
/// The outcome of an operation that returns a value: it worked and has a value, or it failed with an <see cref="Results.Error"/>.
/// </summary>
/// <typeparam name="T">The type of the value of a success.</typeparam>
/// <remarks>
/// A success always has a value. <c>default(Result&lt;T&gt;)</c> is a failure with <see cref="Results.Error.Unspecified"/>.
/// </remarks>
public readonly struct Result<T> : IResultState, IEquatable<Result<T>>
{
    private readonly bool _isSuccess;
    private readonly T? _value;
    private readonly Error _error;

    private Result(bool isSuccess, T? value, Error error)
    {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    /// <inheritdoc />
    public bool IsSuccess => _isSuccess;

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <inheritdoc />
    public Error Error => _isSuccess ? Error.None : _error.IsNone ? Error.Unspecified : _error;

    /// <summary>The value of a success.</summary>
    /// <exception cref="InvalidOperationException">The result is a failure. Check <see cref="IsSuccess"/> first, or use <see cref="TryGetValue"/> or <see cref="Match{TResult}"/>.</exception>
    public T Value
    {
        get
        {
            if (!_isSuccess)
            {
                ThrowValueOfAFailure();
            }

            return _value!;
        }
    }

    // The throw is in its own method so the getter stays small enough for the compiler to inline: a getter with a throw and a
    // formatted message is much less likely to be, and Map, Bind, Tap, Match and every caller read the value through it.
    [DoesNotReturn]
    private void ThrowValueOfAFailure() =>
        throw new InvalidOperationException($"The value of a failure result can not be accessed. The failure is {Error.Code}.");

    internal static Result<T> CreateSuccess(T value) => new(true, value, Error.None);

    /// <summary>
    /// A failure from an error that is already known to have a code: the <c>Error</c> of another failed result, which is never
    /// <see cref="Results.Error.None"/>. Passing a failure on through <c>Map</c> or <c>Bind</c> does not check it again.
    /// </summary>
    internal static Result<T> Propagate(Error error) => new(false, default, error);

    /// <summary>A failed result.</summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> has no code (it is <see cref="Results.Error.None"/>): a failure has to say why.</exception>
    internal static Result<T> CreateFailure(Error error)
    {
        Result.EnsureIsAnError(error);

        return new Result<T>(false, default, error);
    }

    /// <summary>
    /// A success from a value, so a method returning <see cref="Result{T}"/> can <c>return value;</c>. A null value is the
    /// failure <see cref="Results.Error.NullValue"/> rather than an exception: a success always has a value.
    /// </summary>
    public static implicit operator Result<T>(T value) =>
        value is not null ? CreateSuccess(value) : new Result<T>(false, default, Error.NullValue);

    /// <summary>A failure from an error, so a method returning <see cref="Result{T}"/> can <c>return error;</c>.</summary>
    public static implicit operator Result<T>(Error error) => CreateFailure(error);

    /// <summary>Forgets the value: a success stays a success, a failure keeps its error.</summary>
    public static implicit operator Result(Result<T> result) =>
        result.IsSuccess ? Result.Success() : Result.Propagate(result.Error);

    /// <summary>Gets the value when the operation succeeded.</summary>
    /// <param name="value">The value of a success; the default when the result is a failure.</param>
    /// <returns>Whether the operation succeeded.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _isSuccess ? _value : default;

        return _isSuccess;
    }

    /// <summary>Returns the result of <paramref name="onSuccess"/> or of <paramref name="onFailure"/>, whichever applies.</summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>Runs <paramref name="action"/> with the value when the operation succeeded, and returns this result unchanged.</summary>
    public Result<T> Tap(Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsSuccess)
        {
            action(_value!);
        }

        return this;
    }

    /// <summary>Turns the value of a success into another value; a failure is passed on without running <paramref name="map"/>.</summary>
    public Result<TNext> Map<TNext>(Func<T, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (IsFailure)
        {
            return Result<TNext>.Propagate(Error);
        }

        // Through the implicit conversion, so a null result is the NullValue failure rather than an exception.
        Result<TNext> mapped = map(_value!);

        return mapped;
    }

    /// <summary>Runs the next step with the value when the operation succeeded; a failure is passed on without running it.</summary>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next(_value!) : Result<TNext>.Propagate(Error);
    }

    /// <summary>
    /// Turns a success into the failure <paramref name="error"/> when the value does not satisfy <paramref name="predicate"/>;
    /// a failure is passed on without testing it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> has no code and the predicate fails.</exception>
    public Result<T> Ensure(Func<T, bool> predicate, Error error)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        return IsSuccess && !predicate(_value!) ? CreateFailure(error) : this;
    }

    /// <inheritdoc />
    public bool Equals(Result<T> other) =>
        IsSuccess == other.IsSuccess &&
        Error.Equals(other.Error) &&
        (!IsSuccess || EqualityComparer<T>.Default.Equals(_value, other._value));

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsSuccess ? HashCode.Combine(true, _value) : HashCode.Combine(false, Error);

    /// <summary>Whether two results are the same outcome.</summary>
    public static bool operator ==(Result<T> left, Result<T> right) => left.Equals(right);

    /// <summary>Whether two results are different outcomes.</summary>
    public static bool operator !=(Result<T> left, Result<T> right) => !left.Equals(right);

    /// <summary>"Success", or "Failure" with the error code. The value and the description are left out: either can be sensitive or long.</summary>
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error.Code})";
}
