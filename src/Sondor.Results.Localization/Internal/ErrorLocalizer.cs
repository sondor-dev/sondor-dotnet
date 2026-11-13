using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Sondor.Results.Localization.Internal;

/// <summary>
/// Looks an error up in the application's resources, layer by layer, and always ends with the English text the error carries.
/// Nothing here is mutable after construction apart from two bounded caches, so one instance is shared by every request.
/// </summary>
internal sealed class ErrorLocalizer : IErrorLocalizer
{
    private readonly (ResourceSource Source, IStringLocalizer Localizer)[] _resources;
    private readonly string _prefix;
    private readonly string? _sourceCulture;
    private readonly HashSet<string> _showData;
    private readonly SentenceCache _sentences = new();
    private readonly MissingTranslations _missing;

    public ErrorLocalizer(ErrorLocalizationOptions options, IStringLocalizerFactory? factory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _resources = factory is null
            ? []
            : [.. options.Sources.Select(source => (source, source.Open(factory)))];

        _prefix = options.KeyPrefix ?? string.Empty;
        _sourceCulture = string.IsNullOrWhiteSpace(options.SourceCulture) ? null : options.SourceCulture.Trim();
        _showData = new HashSet<string>(options.ShowDataFor, StringComparer.Ordinal);
        _missing = new MissingTranslations(logger, options.MissingTranslationLevel);
    }

    public LocalizedError Localize(Error failure) => LocalizeIn(failure, CultureInfo.CurrentUICulture);

    public LocalizedError Localize(Error failure, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var current = CultureInfo.CurrentUICulture;

        if (ReferenceEquals(current, culture) || string.Equals(current.Name, culture.Name, StringComparison.Ordinal))
        {
            return LocalizeIn(failure, culture);
        }

        // An IStringLocalizer reads the current UI culture and has no way to be told another, so it is set for the call and put back.
        CultureInfo.CurrentUICulture = culture;

        try
        {
            return LocalizeIn(failure, culture);
        }
        finally
        {
            CultureInfo.CurrentUICulture = current;
        }
    }

    private LocalizedError LocalizeIn(Error error, CultureInfo culture)
    {
        if (error.IsNone)
        {
            throw new ArgumentException("Error.None is the error of a success: there is nothing to translate.", nameof(error));
        }

        if (IsSourceCulture(culture))
        {
            var english = EnglishTitles.For(error.Type);

            return new LocalizedError(english, DetailOr(error.Description, english), IsFallback: false);
        }

        var name = culture.Name.Length == 0 ? "invariant" : culture.Name;
        var title = Title(error, name);
        var detail = Detail(error, name);
        var titleText = title ?? EnglishTitles.For(error.Type);

        return new LocalizedError(titleText, detail ?? DetailOr(error.Description, titleText), IsFallback: title is null || detail is null);
    }

    private static string DetailOr(string description, string title) => string.IsNullOrWhiteSpace(description) ? title : description;

    // The culture that needs no lookup: the language the built-in text is written in, and its regional variants. The invariant culture
    // is not a language, so it gets that text too, unless English is looked up like any other.
    private bool IsSourceCulture(CultureInfo culture)
    {
        if (_sourceCulture is null)
        {
            return false;
        }

        var name = culture.Name;

        return name.Length == 0 ||
            name.Equals(_sourceCulture, StringComparison.OrdinalIgnoreCase) ||
            (name.Length > _sourceCulture.Length &&
                name.StartsWith(_sourceCulture, StringComparison.OrdinalIgnoreCase) &&
                name[_sourceCulture.Length] == '-');
    }

    // A title, most specific first: the code, the general template, the kind of failure. Null when none has a translation.
    private string? Title(Error error, string culture)
    {
        var code = error.Code;
        var general = error.Arguments.Template;

        if (Find(code + ".Title", AreaOf(code)) is { } specific)
        {
            return specific;
        }

        if (general is not null && !string.Equals(general, code, StringComparison.Ordinal) &&
            Find(general + ".Title", AreaOf(general)) is { } fromTemplate)
        {
            return fromTemplate;
        }

        var typeKey = "Type." + error.Type + ".Title";
        var fromType = Find(typeKey, "Type");

        if (fromType is null)
        {
            _missing.Missing(culture, typeKey, "Type");
        }

        return fromType;
    }

    // A message, most specific first: the code, then the general template. Null when neither has a usable translation.
    private string? Detail(Error error, string culture)
    {
        var code = error.Code;
        var general = error.Arguments.Template;
        var hasGeneral = general is not null && !string.Equals(general, code, StringComparison.Ordinal);

        var codeKey = code + ".Detail";
        var outcome = TryDetail(error, culture, codeKey, AreaOf(code), out var text);

        if (outcome == Outcome.Found)
        {
            return text;
        }

        // The key a translator would add: the general one when the error has one, because that covers every error of its kind.
        var missingKey = codeKey;
        var missingArea = AreaOf(code);
        var anyUnusable = outcome == Outcome.Unusable;

        if (hasGeneral)
        {
            var generalKey = general + ".Detail";
            var generalOutcome = TryDetail(error, culture, generalKey, AreaOf(general!), out text);

            if (generalOutcome == Outcome.Found)
            {
                return text;
            }

            anyUnusable |= generalOutcome == Outcome.Unusable;
            missingKey = generalKey;
            missingArea = AreaOf(general!);
        }

        // A translation that exists but cannot be used was already reported as that, not as missing.
        if (!anyUnusable)
        {
            _missing.Missing(culture, missingKey, missingArea);
        }

        return null;
    }

    private Outcome TryDetail(Error error, string culture, string key, string area, out string? text)
    {
        var sentence = Find(key, area);

        if (sentence is null)
        {
            text = null;

            return Outcome.Missing;
        }

        text = Render(error, culture, key, sentence);

        return text is null ? Outcome.Unusable : Outcome.Found;
    }

    // Fills a translated sentence with the error's arguments. Rendering is the ErrorTemplate's, so the syntax and the rules for
    // showing data are the ones the English text follows. A translation is data someone wrote, so a mistake in it is reported and
    // skipped, never thrown into a request.
    private string? Render(Error error, string culture, string key, string sentence)
    {
        var general = error.Arguments.Template;
        var showData = _showData.Count > 0 &&
            (_showData.Contains(error.Code) || (general is not null && _showData.Contains(general)));

        var template = _sentences.Get(sentence, showData, out var reason);

        if (template is null)
        {
            _missing.Unusable(culture, key, reason ?? "it is not a valid sentence");

            return null;
        }

        var placeholders = template.Placeholders;

        if (placeholders.Count == 0)
        {
            return template.Create().Description;
        }

        var arguments = new ErrorArgument[placeholders.Count];

        for (var i = 0; i < arguments.Length; i++)
        {
            var name = placeholders[i];

            if (!error.Arguments.TryGetValue(name, out var argument))
            {
                _missing.Unusable(culture, key, $"it names '{name}', which the error has no argument for");

                return null;
            }

            switch (argument.Kind)
            {
                case ErrorArgumentKind.Term:
                    arguments[i] = ErrorArgument.Term(name, Vocabulary(error, culture, name, (string)argument.Value!));
                    break;
                case ErrorArgumentKind.Data when !showData:
                    _missing.Unusable(culture, key, $"it names the data '{name}', which is only shown for the codes listed in ShowDataFor");

                    return null;
                default:
                    // Data the application chose to show, or a sensitive argument (which can only be redacted).
                    arguments[i] = argument;
                    break;
            }
        }

        try
        {
            return template.Create(arguments).Description;
        }
        catch (ArgumentException exception)
        {
            _missing.Unusable(culture, key, exception.Message);

            return null;
        }
    }

    // A word the sentence fills in, translated: in the form one general sentence needs, then plainly, then as written.
    private string Vocabulary(Error error, string culture, string name, string text)
    {
        var general = error.Arguments.Template;

        if (general is not null && Find(general + "." + name + "." + text, area: null) is { } specific)
        {
            return specific;
        }

        var key = name + "." + text;

        if (Find(key, area: null) is { } word)
        {
            return word;
        }

        _missing.Vocabulary(culture, key);

        return text;
    }

    // The first resource that serves the key's area and holds the key. A blank value counts as absent.
    private string? Find(string key, string? area)
    {
        var full = _prefix.Length == 0 ? key : _prefix + key;

        foreach (var (source, localizer) in _resources)
        {
            if (!source.Serves(area))
            {
                continue;
            }

            var found = localizer[full];

            if (!found.ResourceNotFound && !string.IsNullOrWhiteSpace(found.Value))
            {
                return found.Value;
            }
        }

        return null;
    }

    private static string AreaOf(string code)
    {
        var dot = code.IndexOf('.', StringComparison.Ordinal);

        return dot < 0 ? code : code[..dot];
    }

    private enum Outcome
    {
        Found,
        Missing,
        Unusable
    }
}
