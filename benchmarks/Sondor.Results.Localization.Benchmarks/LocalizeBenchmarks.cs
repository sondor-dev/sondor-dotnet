using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Sondor.Results.Localization.Benchmarks;

/// <summary>
/// What it costs to turn an error into text, which happens on the failure path only: the English text (no lookup), a translated
/// error found by its general template with two words of vocabulary, one found by its own code, one with no translation at all,
/// and the overload that follows the current culture, against reading the description the error already carries.
/// </summary>
[MemoryDiagnoser]
public class LocalizeBenchmarks
{
    private static readonly ErrorTemplate OrderNotFound = ErrorTemplates.NotFound.WithCode("Order.NotFound");

    private readonly CultureInfo _english = CultureInfo.GetCultureInfo("en-GB");
    private readonly CultureInfo _french = CultureInfo.GetCultureInfo("fr");
    private readonly CultureInfo _german = CultureInfo.GetCultureInfo("de");
    private readonly Error _notFound = Error.NotFound("Order", "Id");
    private readonly Error _specific = OrderNotFound.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));
    private IErrorLocalizer _localizer = null!;

    /// <summary>Builds a localizer over a dictionary holding a French translation, and silences the missing-translation log.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var entries = new Dictionary<(string Culture, string Key), string>
        {
            [("fr", "Resource.NotFound.Detail")] = "Aucune {Entity} n'a été trouvée pour {Property}.",
            [("fr", "Type.NotFound.Title")] = "Introuvable",
            [("fr", "Entity.Order")] = "commande",
            [("fr", "Property.Id")] = "l'identifiant",
            [("fr", "Order.NotFound.Detail")] = "Cette commande est introuvable.",
            [("fr", "Order.NotFound.Title")] = "Commande introuvable"
        };

        _localizer = new ServiceCollection()
            .AddSingleton<IStringLocalizerFactory>(new DictionaryLocalizerFactory(entries))
            .AddErrorLocalization(options =>
            {
                options.UseResource<Translations>();
                options.MissingTranslationLevel = Microsoft.Extensions.Logging.LogLevel.None;
            })
            .BuildServiceProvider()
            .GetRequiredService<IErrorLocalizer>();

        CultureInfo.DefaultThreadCurrentUICulture = _french;
        CultureInfo.CurrentUICulture = _french;
    }

    /// <summary>The floor: the English description the error already carries.</summary>
    [Benchmark(Baseline = true)]
    public string Description() => _notFound.Description;

    /// <summary>English (the source culture): returned as it is, with no lookup.</summary>
    [Benchmark]
    public LocalizedError English() => _localizer.Localize(_notFound, _english);

    /// <summary>French, found by the general template: a sentence, a title, and two words of vocabulary.</summary>
    [Benchmark]
    public LocalizedError TranslatedByTemplate() => _localizer.Localize(_notFound, _french);

    /// <summary>French, found by the error's own code: a whole sentence with no placeholders.</summary>
    [Benchmark]
    public LocalizedError TranslatedByCode() => _localizer.Localize(_specific, _french);

    /// <summary>German, with no translation at all: every layer is asked, then the English text is used.</summary>
    [Benchmark]
    public LocalizedError NoTranslation() => _localizer.Localize(_notFound, _german);

    /// <summary>The overload that follows the current culture (French here), so no culture is swapped for the call.</summary>
    [Benchmark]
#pragma warning disable CA1304 // Specify CultureInfo: this benchmark is about the overload that reads the current culture.
    public LocalizedError TranslatedInCurrentCulture() => _localizer.Localize(_notFound);
#pragma warning restore CA1304
}
