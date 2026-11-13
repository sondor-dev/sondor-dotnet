namespace Sondor.AspNetCore;

/// <summary>
/// The exceptions an application answers with a specific error. Every exception that is not listed here becomes a generic 500 whose
/// message is never sent to the caller.
/// </summary>
public sealed class ExceptionMappings
{
    private readonly Dictionary<Type, Func<Exception, Error>> _maps = [];

    /// <summary>
    /// Answers <typeparamref name="TException"/>, and the exceptions derived from it, with an <see cref="Error"/>. The error is rendered
    /// like any other: its type decides the status, its code becomes <c>errorCode</c>, and its description is shown to the caller, so
    /// it must be safe to show. An exact match wins over a base type; mapping the same type again replaces the first mapping.
    /// </summary>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <param name="map">Builds the error for an exception.</param>
    /// <returns>These mappings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
    public ExceptionMappings Map<TException>(Func<TException, Error> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);

        _maps[typeof(TException)] = exception => map((TException)exception);

        return this;
    }

    internal bool TryMap(Exception exception, out Error error)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (_maps.TryGetValue(type, out var map))
            {
                error = map(exception);

                return true;
            }
        }

        error = default;

        return false;
    }
}
