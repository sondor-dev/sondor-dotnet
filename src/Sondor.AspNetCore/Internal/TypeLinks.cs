using System.Collections.Concurrent;

namespace Sondor.AspNetCore.Internal;

/// <summary>
/// Builds the documentation link of an error code from <see cref="ErrorResponseOptions.TypeUriTemplate"/>. A link is a few strings to
/// build and an application has few codes, so each is built once and kept. The cache is bounded, so a code that is made up from
/// something a caller sent can never grow it without limit: past the limit a link is built each time.
/// </summary>
internal static class TypeLinks
{
    private const int Capacity = 512;

    private static readonly ConcurrentDictionary<(string Template, string Code), string> Cache = new();

    private static int _count;

    /// <summary>How many links are kept. For tests.</summary>
    public static int Count => Volatile.Read(ref _count);

    public static string Expand(string template, string code)
    {
        if (Cache.TryGetValue((template, code), out var cached))
        {
            return cached;
        }

        var link = template.Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal);

        if (Volatile.Read(ref _count) < Capacity && Cache.TryAdd((template, code), link))
        {
            Interlocked.Increment(ref _count);
        }

        return link;
    }
}
