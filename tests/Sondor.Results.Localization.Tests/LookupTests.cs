using System.Globalization;

namespace Sondor.Results.Localization.Tests;

[TestFixture]
public sealed class LookupTests
{
    private static readonly ErrorTemplate OrderNotFound = ErrorTemplates.NotFound.WithCode("Order.NotFound");

    private FakeLocalizerFactory _factory = null!;
    private FakeResource _shared = null!;
    private CapturingLoggerFactory _logs = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new FakeLocalizerFactory();
        _shared = _factory.Resource<SharedResource>();
        _logs = new CapturingLoggerFactory();
    }

    [TearDown]
    public void TearDown() => _logs.Dispose();

    // The overload that reads the current culture is what a web request uses; a test that means to use it says so here once.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304:Specify CultureInfo",
        Justification = "These tests are about the overload that follows the current UI culture.")]
    private static LocalizedError InCurrentCulture(IErrorLocalizer localizer, Error error) => localizer.Localize(error);

    private IErrorLocalizer Localizer(Action<ErrorLocalizationOptions>? configure = null) =>
        Setup.Localizer(_factory, _logs, options =>
        {
            options.UseResource<SharedResource>();
            configure?.Invoke(options);
        });

    private static Error SpecificNotFound() => OrderNotFound.Create(
        ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

    [Test]
    public void English_Should_Be_Returned_As_It_Is_Without_A_Lookup()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable.");

        foreach (var culture in new[] { "en", "en-GB", "en-US", "" })
        {
            var text = Localizer().Localize(Error.NotFound("Order", "Id"), CultureInfo.GetCultureInfo(culture));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(text.Title, Is.EqualTo("Not found"), culture);
                Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."), culture);
                Assert.That(text.IsFallback, Is.False, culture);
            }
        }

        Assert.That(_shared.Lookups, Is.Empty);
    }

    [Test]
    public void A_Language_That_Only_Starts_With_The_Source_Culture_Should_Still_Be_Looked_Up()
    {
        // "eng" is not "en-": the source culture matches the language and its regional variants, nothing else.
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable.");

        var text = Localizer(options => options.SourceCulture = "f").Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Introuvable."));
    }

    [Test]
    public void English_Should_Be_Looked_Up_Like_Any_Language_When_There_Is_No_Source_Culture()
    {
        _shared.Add("en", "Resource.NotFound.Detail", "That {Entity} does not exist.");

        var text = Localizer(options => options.SourceCulture = null)
            .Localize(Error.NotFound("Order", "Id"), CultureInfo.GetCultureInfo("en"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("That Order does not exist."));
            Assert.That(text.IsFallback, Is.True, "the title has no English entry in the resources");
        }
    }

    [Test]
    public void The_Message_For_A_Code_Should_Beat_The_Message_For_Its_Template()
    {
        _shared.Add("fr", "Order.NotFound.Detail", "La commande est introuvable.");
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} trouvée.");

        var text = Localizer().Localize(SpecificNotFound(), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("La commande est introuvable."));
    }

    [Test]
    public void The_Message_For_The_Template_Should_Be_Used_When_The_Code_Has_None()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} n'a été trouvée pour {Property}.");

        var text = Localizer().Localize(SpecificNotFound(), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Aucune Order n'a été trouvée pour Id."));
    }

    [Test]
    public void An_Error_Without_A_Template_Should_Be_Looked_Up_By_Its_Code_Alone()
    {
        _shared.Add("fr", "Order.Locked.Detail", "La commande est verrouillée.");

        var text = Localizer().Localize(new Error("Order.Locked", "The order is locked.", ErrorType.Conflict), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("La commande est verrouillée."));
    }

    [Test]
    public void The_English_Text_Should_Be_Used_And_Flagged_When_Nothing_Is_Translated()
    {
        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Title, Is.EqualTo("Not found"));
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(text.IsFallback, Is.True);
        }
    }

    [Test]
    public void A_Translated_Error_Should_Not_Be_Flagged_When_Both_Its_Title_And_Message_Are_Found()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");
        _shared.Add("fr", "Type.NotFound.Title", "Introuvable");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Title, Is.EqualTo("Introuvable"));
            Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
            Assert.That(text.IsFallback, Is.False);
        }
    }

    [Test]
    public void A_Message_Alone_Should_Still_Be_Flagged_Because_The_Title_Fell_Back()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Title, Is.EqualTo("Not found"));
            Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
            Assert.That(text.IsFallback, Is.True);
        }
    }

    [Test]
    public void A_Title_Should_Come_From_The_Code_Then_The_Template_Then_The_Kind_Of_Failure()
    {
        var error = SpecificNotFound();

        _shared.Add("fr", "Type.NotFound.Title", "Par type");
        Assert.That(Localizer().Localize(error, Setup.French).Title, Is.EqualTo("Par type"));

        _shared.Add("fr", "Resource.NotFound.Title", "Par modèle");
        Assert.That(Localizer().Localize(error, Setup.French).Title, Is.EqualTo("Par modèle"));

        _shared.Add("fr", "Order.NotFound.Title", "Par code");
        Assert.That(Localizer().Localize(error, Setup.French).Title, Is.EqualTo("Par code"));
    }

    [Test]
    public void The_Words_Of_A_Sentence_Should_Be_Translated_As_Vocabulary()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} n'a été trouvée pour {Property}.");
        _shared.Add("fr", "Entity.Order", "commande");
        _shared.Add("fr", "Property.Id", "identifiant");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Aucune commande n'a été trouvée pour identifiant."));
    }

    [Test]
    public void A_Word_In_The_Form_One_Sentence_Needs_Should_Beat_The_Plain_Word()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} trouvée.");
        _shared.Add("fr", "Entity.Order", "commande");
        _shared.Add("fr", "Resource.NotFound.Entity.Order", "commande-ci");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Aucune commande-ci trouvée."));
    }

    [Test]
    public void A_Word_Without_A_Translation_Should_Stay_As_Written_And_Not_Count_As_Missing()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} trouvée.");
        _shared.Add("fr", "Type.NotFound.Title", "Introuvable");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("Aucune Order trouvée."));
            Assert.That(text.IsFallback, Is.False);
            Assert.That(_logs.Entries.Where(entry => entry.EventId is 1 or 2), Is.Empty, "nothing is reported as missing or unusable");
        }
    }

    [Test]
    public void A_Translated_Sentence_Should_Be_Free_To_Leave_A_Placeholder_Out()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
    }

    [Test]
    public void A_Regional_Culture_Should_Find_The_Translation_Of_Its_Language()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), CultureInfo.GetCultureInfo("fr-CA"));

        Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
    }

    [Test]
    public void A_Blank_Translation_Should_Count_As_Missing()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "   ");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(text.IsFallback, Is.True);
        }
    }

    [Test]
    public void The_Key_Prefix_Should_Go_In_Front_Of_Every_Key()
    {
        _shared.Add("fr", "Errors.Resource.NotFound.Detail", "Introuvable : {Entity}.");
        _shared.Add("fr", "Errors.Entity.Order", "commande");
        _shared.Add("fr", "Errors.Type.NotFound.Title", "Introuvable");

        var text = Localizer(options => options.KeyPrefix = "Errors.").Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Title, Is.EqualTo("Introuvable"));
            Assert.That(text.Detail, Is.EqualTo("Introuvable : commande."));
        }
    }

    [Test]
    public void The_Title_And_Message_Should_Never_Be_Empty_Even_For_An_Error_With_No_Description()
    {
        var text = Localizer().Localize(new Error("Order.Gone", string.Empty, ErrorType.Conflict), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Title, Is.EqualTo("Conflict"));
            Assert.That(text.Detail, Is.EqualTo("Conflict"));
        }
    }

    [Test]
    public void An_Unknown_Kind_Of_Failure_Should_Get_A_General_Title()
    {
        var text = Localizer().Localize(new Error("Order.Odd", "Odd.", (ErrorType)99), Setup.French);

        Assert.That(text.Title, Is.EqualTo("Request failed"));
    }

    [Test]
    public void Every_Kind_Of_Failure_Should_Have_An_English_Title()
    {
        var titles = Enum.GetValues<ErrorType>()
            .Select(type => InCurrentCulture(Localizer(), new Error("Area.Reason", "A reason.", type)).Title)
            .ToList();

        Assert.That(titles, Has.All.Not.Empty);
        Assert.That(titles, Is.Unique);
    }

    [Test]
    public void The_Error_Of_A_Success_Should_Have_Nothing_To_Translate()
    {
        Assert.That(() => InCurrentCulture(Localizer(), Error.None), Throws.ArgumentException);
        Assert.That(() => Localizer().Localize(Error.None, Setup.French), Throws.ArgumentException);
    }

    [Test]
    public void The_Current_Culture_Should_Be_Used_When_None_Is_Given()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");
        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = Setup.French;

            Assert.That(InCurrentCulture(Localizer(), Error.NotFound("Order", "Id")).Detail, Is.EqualTo("Introuvable : Order."));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Test]
    public void A_Given_Culture_Should_Be_Used_For_The_Call_And_The_Current_Culture_Put_Back()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");
        var before = CultureInfo.CurrentUICulture;

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
            Assert.That(CultureInfo.CurrentUICulture, Is.SameAs(before));
        }
    }

    [Test]
    public void The_Current_Culture_Should_Be_Put_Back_Even_When_The_Call_Throws()
    {
        var before = CultureInfo.CurrentUICulture;

        Assert.That(() => Localizer().Localize(Error.None, Setup.German), Throws.ArgumentException);
        Assert.That(CultureInfo.CurrentUICulture, Is.SameAs(before));
    }

    [Test]
    public void A_Missing_Culture_Should_Be_Refused()
    {
        Assert.That(() => Localizer().Localize(Error.NotFound("Order", "Id"), null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Calls_From_Many_Threads_Should_Agree()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} n'a été trouvée pour {Property}.");
        _shared.Add("fr", "Entity.Order", "commande");
        var localizer = Localizer();
        var error = Error.NotFound("Order", "Id");

        var results = new string[2000];
        Parallel.For(0, results.Length, i => results[i] = localizer.Localize(error, Setup.French).Detail);

        Assert.That(results, Has.All.EqualTo("Aucune commande n'a été trouvée pour Id."));
    }
}
