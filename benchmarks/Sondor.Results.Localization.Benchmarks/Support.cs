using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Sondor.Results.Localization.Benchmarks;

/// <summary>A translation store that is a dictionary, so what is measured is the localizer and not a file or a database.</summary>
internal sealed class DictionaryLocalizerFactory(Dictionary<(string Culture, string Key), string> entries) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => new Localizer(entries);

    public IStringLocalizer Create(string baseName, string location) => new Localizer(entries);

    private sealed class Localizer(Dictionary<(string Culture, string Key), string> entries) : IStringLocalizer
    {
        public LocalizedString this[string name]
        {
            get
            {
                for (var culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent)
                {
                    if (entries.TryGetValue((culture.Name, name), out var value))
                    {
                        return new LocalizedString(name, value);
                    }
                }

                return new LocalizedString(name, name, resourceNotFound: true);
            }
        }

        public LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

internal sealed class Translations;
