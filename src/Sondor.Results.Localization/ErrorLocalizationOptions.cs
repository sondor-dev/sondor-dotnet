using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Sondor.Results.Localization.Internal;

namespace Sondor.Results.Localization;

/// <summary>
/// How errors are translated. Every setting has a default, so <c>AddErrorLocalization()</c> alone is a valid setup: until a resource is
/// added with <c>UseResource</c>, nothing is looked up and every error keeps its English text.
/// </summary>
public sealed class ErrorLocalizationOptions
{
    private readonly List<ResourceSource> _sources = [];

    /// <summary>
    /// Put in front of every key, so the error text can share a resource file with the rest of an application, for example
    /// <c>Errors.</c> makes the key <c>Errors.Order.NotFound.Detail</c>. Empty by default.
    /// </summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The language the built-in text is written in. For this culture, and its regional variants (<c>en</c> covers <c>en-GB</c>), the
    /// English text is returned as it is without any lookup, which is quicker and is what you want unless you reword English in your
    /// own resources. Set it to <see langword="null"/> to look English up like any other language. <c>en</c> by default.
    /// </summary>
    public string? SourceCulture { get; set; } = "en";

    /// <summary>
    /// How loudly a missing translation is logged, once for each culture and key. <see cref="LogLevel.Warning"/> by default; use
    /// <see cref="LogLevel.Debug"/> or <see cref="LogLevel.None"/> for an application that is deliberately translated in part. The
    /// translation counter and <see cref="LocalizedError.IsFallback"/> are not affected.
    /// </summary>
    public LogLevel MissingTranslationLevel { get; set; } = LogLevel.Warning;

    /// <summary>
    /// The error codes (or general template codes, such as <c>Resource.NotFound</c>) whose translated text may show a
    /// <see cref="ErrorArgumentKind.Data"/> argument, which came from a caller. Empty by default: data is never shown. The value
    /// is shown on one line, cut to 64 characters.
    /// </summary>
    public ICollection<string> ShowDataFor { get; } = new HashSet<string>(StringComparer.Ordinal);

    internal IReadOnlyList<ResourceSource> Sources => _sources;

    /// <summary>
    /// Adds a resource to search for translations, such as the <c>.resx</c> file that belongs to <typeparamref name="TResource"/>.
    /// Call it as many times as you like: resources are searched in the order added and the first one holding a key wins, so
    /// one file does not have to hold every message of an application.
    /// </summary>
    /// <typeparam name="TResource">The marker type of the resource, as with <c>IStringLocalizer&lt;T&gt;</c>.</typeparam>
    /// <param name="areas">
    /// The areas of the error codes this resource holds: the part of a code before the dot, so <c>Order</c> for
    /// <c>Order.NotFound</c>. A resource with areas is only asked about keys of those codes. With none, it is asked about every key
    /// (the general templates, the titles, the vocabulary).
    /// </param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">An area is blank or contains a dot.</exception>
    public ErrorLocalizationOptions UseResource<TResource>(params string[] areas) =>
        Add(factory => factory.Create(typeof(TResource)), areas);

    /// <summary>Adds a resource to search for translations, by name. See <see cref="UseResource{TResource}"/>.</summary>
    /// <param name="baseName">The base name of the resource.</param>
    /// <param name="location">The location (the assembly name) of the resource.</param>
    /// <param name="areas">The areas of the error codes this resource holds. With none, it is asked about every key.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException">A name or an area is blank, or an area contains a dot.</exception>
    public ErrorLocalizationOptions UseResource(string baseName, string location, params string[] areas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        return Add(factory => factory.Create(baseName, location), areas);
    }

    private ErrorLocalizationOptions Add(Func<IStringLocalizerFactory, IStringLocalizer> create, string[] areas)
    {
        ArgumentNullException.ThrowIfNull(areas);

        foreach (var area in areas)
        {
            if (string.IsNullOrWhiteSpace(area) || area.Contains('.', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"The area '{area}' is not valid: it is the part of a code before the dot, such as 'Order' for 'Order.NotFound'.",
                    nameof(areas));
            }
        }

        _sources.Add(new ResourceSource(create, [.. areas]));

        return this;
    }
}
