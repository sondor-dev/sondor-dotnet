namespace Sondor.Http;

/// <summary>
/// The exceptions that are reported as an <see cref="Error"/> instead of being thrown. An exception that is not mapped, here or by
/// the package, is a mistake in the code and is thrown as usual.
/// </summary>
public sealed class HttpExceptionMappings
{
    private readonly Dictionary<Type, Func<Exception, Error>> _maps = [];

    /// <summary>
    /// Reports <typeparamref name="TException"/>, and the exceptions derived from it, as an <see cref="Error"/>. An exact match wins over a
    /// base type, and a mapping of your own wins over the package's. Mapping the same type again replaces the first mapping.
    /// </summary>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <param name="map">Builds the error. Its description should be safe to show.</param>
    /// <returns>These mappings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="map"/> is null.</exception>
    public HttpExceptionMappings Map<TException>(Func<TException, Error> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);

        _maps[typeof(TException)] = exception => map((TException)exception);

        return this;
    }

    internal bool TryMap(Exception exception, out Error error)
    {
        if (_maps.Count > 0)
        {
            for (var type = exception.GetType(); type is not null; type = type.BaseType)
            {
                if (_maps.TryGetValue(type, out var map))
                {
                    error = map(exception);

                    return true;
                }
            }
        }

        error = default;

        return false;
    }
}
