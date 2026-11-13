using Microsoft.Extensions.Localization;

namespace Sondor.Results.Localization.Internal;

/// <summary>One resource to search: how to open it, and the areas of the codes it holds (none: every key).</summary>
internal sealed class ResourceSource(Func<IStringLocalizerFactory, IStringLocalizer> create, string[] areas)
{
    public string[] Areas { get; } = areas;

    public IStringLocalizer Open(IStringLocalizerFactory factory) => create(factory);

    /// <summary>
    /// Whether this resource is asked about a key. A key with no area (a word of the vocabulary) is only asked of a resource that
    /// holds every key.
    /// </summary>
    public bool Serves(string? area) => Areas.Length == 0 || (area is not null && Array.IndexOf(Areas, area) >= 0);
}
