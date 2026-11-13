using System.Diagnostics.CodeAnalysis;

namespace Sondor.Results;

/// <summary>
/// The outcome of an operation that returns nothing: it worked, or it failed with an <see cref="Results.Error"/>.
/// </summary>
/// <remarks>
/// <c>default(Result)</c> is a failure with <see cref="Results.Error.Unspecified"/>, not a success: a value nobody
/// initialised must not look like a good one.
/// </remarks>
public readonly struct Result : IResultState, IEquatable<Result>
{
    private readonly bool _isSuccess;
    private readonly Error _error;

    private Result(bool isSuccess, Error error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <inheritdoc />
    public bool IsSuccess => _isSuccess;

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <inheritdoc />
    public Error Error => _isSuccess ? Error.None : _error.IsNone ? Error.Unspecified : _error;

    /// <summary>A successful result.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>A successful result carrying <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null. A success always has a value; use the non-generic <see cref="Success()"/> for "nothing to return".</exception>
    public static Result<T> Success<T>(T value)
    {
        // Not ThrowIfNull: that takes an object, so it would box every value type that passes through here.
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "A success always has a value.");
        }

        return Result<T>.CreateSuccess(value);
    }

    /// <summary>A failed result.</summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> has no code (it is <see cref="Results.Error.None"/>): a failure has to say why.</exception>
    public static Result Failure(Error error)
    {
        EnsureIsAnError(error);

        return new Result(false, error);
    }

    /// <summary>A failed result of <typeparamref name="T"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> has no code.</exception>
    public static Result<T> Failure<T>(Error error) => Result<T>.CreateFailure(error);

    /// <summary>A failure from an error, so a method returning <see cref="Result"/> can <c>return error;</c>.</summary>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Returns the result of <paramref name="onSuccess"/> or of <paramref name="onFailure"/>, whichever applies.</summary>
    public TResult Match<TResult>(Func<TResult> onSuccess, Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>Runs <paramref name="action"/> when the operation succeeded, and returns this result unchanged.</summary>
    public Result Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsSuccess)
        {
            action();
        }

        return this;
    }

    /// <summary>Runs the next step when the operation succeeded; a failure is passed on without running it.</summary>
    public Result Bind(Func<Result> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next() : this;
    }

    /// <summary>Runs the next step, which produces a value, when the operation succeeded; a failure is passed on without running it.</summary>
    public Result<T> Bind<T>(Func<Result<T>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return IsSuccess ? next() : Result<T>.Propagate(Error);
    }

    /// <summary>Produces a value when the operation succeeded; a failure is passed on without running <paramref name="map"/>.</summary>
    public Result<T> Map<T>(Func<T> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (IsFailure)
        {
            return Result<T>.Propagate(Error);
        }

        // Through the implicit conversion, so a null result is the NullValue failure rather than an exception.
        Result<T> mapped = map();

        return mapped;
    }

    /// <inheritdoc />
    public bool Equals(Result other) => IsSuccess == other.IsSuccess && Error.Equals(other.Error);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsSuccess, Error);

    /// <summary>Whether two results are the same outcome.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Whether two results are different outcomes.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <summary>"Success", or "Failure" with the error code. The description is left out: it can be long.</summary>
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error.Code})";

    /// <summary>
    /// A failure from an error that is already known to have a code: the <c>Error</c> of another failed result, which is never
    /// <see cref="Results.Error.None"/>. Passing a failure on through <c>Map</c>, <c>Bind</c> or a conversion does not check it again.
    /// </summary>
    internal static Result Propagate(Error error) => new(false, error);

    internal static void EnsureIsAnError(Error error)
    {
        if (error.IsNone)
        {
            ThrowErrorHasNoCode();
        }
    }

    // The throw is in its own method so the check above stays small enough for the compiler to inline into every caller: a method
    // that contains a throw with a message is much less likely to be inlined, and this runs for every failure that is made.
    [DoesNotReturn]
    private static void ThrowErrorHasNoCode() =>
        throw new ArgumentException("A failure needs an error with a code; Error.None is the error of a success.", "error");
}
