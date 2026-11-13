using Microsoft.Extensions.Logging;

namespace Sondor.Results.Localization.Tests;

/// <summary>What a translation may show, and what happens when a translation is wrong: a mistake is reported and skipped, never thrown.</summary>
[TestFixture]
public sealed class DataAndTranslationMistakeTests
{
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

    private IErrorLocalizer Localizer(Action<ErrorLocalizationOptions>? configure = null) =>
        Setup.Localizer(_factory, _logs, options =>
        {
            options.UseResource<SharedResource>();
            configure?.Invoke(options);
        });

    [Test]
    public void Data_Should_Never_Be_Shown_By_Default_And_The_Next_Layer_Used()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} pour {Value}.");

        var text = Localizer().Localize(Error.NotFound("Order", "Id", "secret-42"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(text.Detail, Does.Not.Contain("secret-42"));
            Assert.That(text.IsFallback, Is.True);
            Assert.That(_logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning && entry.Message.Contains("ShowDataFor", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void Data_Should_Be_Shown_For_A_Listed_Code_On_One_Short_Line()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} pour {Value}.");
        var value = "line one\nline two " + new string('x', 100);

        var text = Localizer(options => options.ShowDataFor.Add("Resource.NotFound"))
            .Localize(Error.NotFound("Order", "Id", value), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Does.StartWith("Aucune Order pour line one line two xxx"));
            Assert.That(text.Detail, Does.Not.Contain('\n'));
            Assert.That(text.Detail, Does.EndWith("….").Or.Contain("…"));
            Assert.That(text.Detail.Length, Is.LessThan(100));
        }
    }

    [Test]
    public void Data_Should_Be_Shown_For_A_Listed_Specific_Code()
    {
        _shared.Add("fr", "Order.NotFound.Detail", "Commande {Value} introuvable.");
        var template = ErrorTemplates.NotFound.WithCode("Order.NotFound");
        var error = template.Create(
            ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"), ErrorArgument.Data("Value", 42));

        var text = Localizer(options => options.ShowDataFor.Add("Order.NotFound")).Localize(error, Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Commande 42 introuvable."));
    }

    [Test]
    public void A_Sensitive_Argument_Should_Only_Ever_Be_Redacted()
    {
        _shared.Add("fr", "Account.Locked.Detail", "Le compte {Holder} est verrouillé.");
        var template = new ErrorTemplate("Account.Locked", ErrorType.Conflict, "The account of {Holder} is locked.");
        var error = template.Create(ErrorArgument.Sensitive("Holder"));

        var text = Localizer().Localize(error, Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Le compte [redacted] est verrouillé."));
    }

    [TestCase("Aucune {Entity n'a été trouvée.", "never closed")]
    [TestCase("Aucune {0} trouvée.", "not a placeholder name")]
    [TestCase("Aucune {Entity:N2} trouvée.", "not a placeholder name")]
    [TestCase("Aucune } trouvée.", "no matching")]
    [TestCase("Aucune {Entity} et {Entity} trouvées.", "twice")]
    public void A_Sentence_That_Does_Not_Parse_Should_Be_Skipped_And_Reported(string sentence, string reasonPart)
    {
        _shared.Add("fr", "Resource.NotFound.Detail", sentence);

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(text.IsFallback, Is.True);
            Assert.That(_logs.Entries.Any(entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("Resource.NotFound.Detail", StringComparison.Ordinal) &&
                entry.Message.Contains(reasonPart, StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void A_Sentence_That_Names_Something_The_Error_Does_Not_Have_Should_Be_Skipped()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Thing} trouvée.");

        var text = Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(_logs.Entries.Any(entry => entry.Message.Contains("'Thing'", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void A_Sentence_That_Is_Wrong_For_A_Code_Should_Fall_Through_To_The_Template_Without_Being_Called_Missing()
    {
        _shared.Add("fr", "Order.NotFound.Detail", "Cassé {");
        _shared.Add("fr", "Resource.NotFound.Detail", "Introuvable : {Entity}.");
        _shared.Add("fr", "Type.NotFound.Title", "Introuvable");
        var template = ErrorTemplates.NotFound.WithCode("Order.NotFound");
        var error = template.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

        var text = Localizer().Localize(error, Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("Introuvable : Order."));
            Assert.That(_logs.Entries.Count(entry => entry.EventId == 2), Is.EqualTo(1), "one report for the unusable translation");
            Assert.That(_logs.Entries.Count(entry => entry.EventId == 1), Is.Zero, "and it is not also called missing");
        }
    }

    [Test]
    public void A_Mistake_Should_Be_Reported_Once_Per_Culture_And_Key()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Cassé {");
        var localizer = Localizer();

        for (var i = 0; i < 5; i++)
        {
            localizer.Localize(Error.NotFound("Order", "Id"), Setup.French);
        }

        Assert.That(_logs.Entries.Count(entry => entry.EventId == 2), Is.EqualTo(1));
    }

    [Test]
    public void A_Missing_Translation_Should_Be_Reported_Once_Per_Culture_And_Key_With_The_Key_To_Add()
    {
        var localizer = Localizer();

        for (var i = 0; i < 5; i++)
        {
            localizer.Localize(Error.NotFound("Order", "Id"), Setup.French);
        }

        localizer.Localize(Error.NotFound("Order", "Id"), Setup.German);

        var missing = _logs.Entries.Where(entry => entry.EventId == 1).Select(entry => entry.Message).ToList();

        Assert.That(missing, Has.Some.Contain("'Resource.NotFound.Detail'").And.Contain("'fr'"));
        Assert.That(missing, Has.Some.Contain("'Resource.NotFound.Detail'").And.Contain("'de'"));
        Assert.That(missing, Has.Some.Contain("'Type.NotFound.Title'").And.Contain("'fr'"));
        Assert.That(missing, Has.Count.EqualTo(4), "a detail and a title, for each of two cultures, however often it happens");
    }

    [Test]
    public void The_Key_To_Add_Should_Be_The_General_One_When_The_Error_Has_A_Template()
    {
        var template = ErrorTemplates.NotFound.WithCode("Order.NotFound");
        var error = template.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

        Localizer().Localize(error, Setup.French);

        Assert.That(_logs.Entries.Where(entry => entry.EventId == 1).Select(entry => entry.Message),
            Has.Some.Contain("'Resource.NotFound.Detail'"));
    }

    [Test]
    public void The_Level_Of_A_Missing_Translation_Should_Follow_The_Option()
    {
        Localizer(options => options.MissingTranslationLevel = LogLevel.Debug).Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(_logs.Entries.Where(entry => entry.EventId == 1).Select(entry => entry.Level), Has.All.EqualTo(LogLevel.Debug));
    }

    [Test]
    public void A_Missing_Translation_Should_Log_Nothing_When_The_Level_Is_None()
    {
        Localizer(options => options.MissingTranslationLevel = LogLevel.None).Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(_logs.Entries, Is.Empty);
    }

    [Test]
    public void A_Word_Without_A_Translation_Should_Be_Reported_At_Debug_Level_Only()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} trouvée.");

        Localizer().Localize(Error.NotFound("Order", "Id"), Setup.French);

        Assert.That(_logs.Entries.Any(entry =>
            entry.EventId == 3 && entry.Level == LogLevel.Debug && entry.Message.Contains("'Entity.Order'", StringComparison.Ordinal)), Is.True);
    }
}
