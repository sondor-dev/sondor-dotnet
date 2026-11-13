using System.Diagnostics.Metrics;

namespace Sondor.Results.Localization.Tests;

/// <summary>Several resource files, the areas that route a key to one of them, and the counter of missing translations.</summary>
[TestFixture]
public sealed class SourcesAndMetricsTests
{
    private static readonly ErrorTemplate OrderNotFound = ErrorTemplates.NotFound.WithCode("Order.NotFound");
    private static readonly ErrorTemplate InvoiceNotFound = ErrorTemplates.NotFound.WithCode("Invoice.NotFound");

    private FakeLocalizerFactory _factory = null!;
    private FakeResource _orders = null!;
    private FakeResource _billing = null!;
    private FakeResource _shared = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new FakeLocalizerFactory();
        _orders = _factory.Resource<OrderResource>();
        _billing = _factory.Resource<BillingResource>();
        _shared = _factory.Resource<SharedResource>();
    }

    private IErrorLocalizer Localizer() => Setup.Localizer(_factory, configure: options => options
        .UseResource<OrderResource>("Order", "Cart")
        .UseResource<BillingResource>("Invoice")
        .UseResource<SharedResource>());

    private static Error Order() => OrderNotFound.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

    private static Error Invoice() => InvoiceNotFound.Create(ErrorArgument.Term("Entity", "Invoice"), ErrorArgument.Term("Property", "Id"));

    [Test]
    public void A_Code_Should_Be_Looked_Up_In_The_Resource_That_Holds_Its_Area()
    {
        _orders.Add("fr", "Order.NotFound.Detail", "Commande introuvable.");
        _billing.Add("fr", "Invoice.NotFound.Detail", "Facture introuvable.");

        var localizer = Localizer();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(localizer.Localize(Order(), Setup.French).Detail, Is.EqualTo("Commande introuvable."));
            Assert.That(localizer.Localize(Invoice(), Setup.French).Detail, Is.EqualTo("Facture introuvable."));
        }
    }

    [Test]
    public void A_Resource_With_Areas_Should_Not_Be_Asked_About_Keys_Of_Other_Areas()
    {
        _orders.Add("fr", "Order.NotFound.Detail", "Commande introuvable.");

        Localizer().Localize(Invoice(), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_orders.Lookups, Is.Empty, "Invoice.* is not an Order or Cart area");
            Assert.That(_billing.Lookups, Does.Contain("Invoice.NotFound.Detail"));
        }
    }

    [Test]
    public void The_General_Templates_And_The_Vocabulary_Should_Come_From_A_Resource_Without_Areas()
    {
        _shared.Add("fr", "Resource.NotFound.Detail", "Aucune {Entity} trouvée.");
        _shared.Add("fr", "Entity.Invoice", "facture");
        _billing.Add("fr", "Entity.Invoice", "ne doit pas être lu");

        var text = Localizer().Localize(Invoice(), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Aucune facture trouvée."));
        Assert.That(_billing.Lookups, Has.None.StartWith("Entity."), "vocabulary has no area, so a resource with areas is never asked");
    }

    [Test]
    public void The_First_Resource_Holding_A_Key_Should_Win()
    {
        _orders.Add("fr", "Order.NotFound.Detail", "Première.");
        _shared.Add("fr", "Order.NotFound.Detail", "Seconde.");

        Assert.That(Localizer().Localize(Order(), Setup.French).Detail, Is.EqualTo("Première."));
    }

    [Test]
    public void A_Key_Missing_From_The_First_Resource_Should_Be_Found_In_The_Next()
    {
        _shared.Add("fr", "Order.NotFound.Detail", "Seconde.");

        Assert.That(Localizer().Localize(Order(), Setup.French).Detail, Is.EqualTo("Seconde."));
    }

    [Test]
    public void A_Resource_Can_Be_Added_By_Name()
    {
        _factory.Resource("Errors").Add("fr", "Order.NotFound.Detail", "Par nom.");

        var localizer = Setup.Localizer(_factory, configure: options => options.UseResource("Errors", "MyApp"));

        Assert.That(localizer.Localize(Order(), Setup.French).Detail, Is.EqualTo("Par nom."));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("Order.NotFound")]
    public void An_Area_Should_Be_The_Part_Of_A_Code_Before_The_Dot(string area)
    {
        var options = new ErrorLocalizationOptions();

        Assert.That(() => options.UseResource<OrderResource>(area), Throws.ArgumentException);
        Assert.That(() => options.UseResource("Errors", "MyApp", area), Throws.ArgumentException);
    }

    [Test]
    public void A_Resource_Name_And_Areas_Should_Be_Given()
    {
        var options = new ErrorLocalizationOptions();

        Assert.That(() => options.UseResource(" ", "MyApp"), Throws.ArgumentException);
        Assert.That(() => options.UseResource("Errors", " "), Throws.ArgumentException);
        Assert.That(() => options.UseResource<OrderResource>(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void A_Missing_Translation_Should_Be_Counted_Every_Time_With_The_Culture_And_The_Area()
    {
        var measurements = new List<(long Value, string Culture, string Area)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Sondor.Results.Localization")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var culture = tags.ToArray().Single(tag => tag.Key == "culture").Value?.ToString() ?? string.Empty;
            var area = tags.ToArray().Single(tag => tag.Key == "area").Value?.ToString() ?? string.Empty;

            lock (measurements)
            {
                measurements.Add((value, culture, area));
            }
        });
        listener.Start();

        var localizer = Localizer();
        var spanish = System.Globalization.CultureInfo.GetCultureInfo("es");

        for (var i = 0; i < 3; i++)
        {
            localizer.Localize(Order(), spanish);
        }

        listener.Dispose();

        List<(long Value, string Culture, string Area)> forSpanish;

        lock (measurements)
        {
            forSpanish = [.. measurements.Where(measurement => measurement.Culture == "es")];
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(forSpanish.Count(measurement => measurement.Area == "Resource"), Is.EqualTo(3), "the detail, counted every time");
            Assert.That(forSpanish.Count(measurement => measurement.Area == "Type"), Is.EqualTo(3), "the title, counted every time");
            Assert.That(forSpanish.Sum(measurement => measurement.Value), Is.EqualTo(6));
        }
    }

    [Test]
    public void A_Translated_Error_Should_Not_Be_Counted()
    {
        var count = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Sondor.Results.Localization")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (tags.ToArray().Any(tag => tag.Key == "culture" && "it".Equals(tag.Value)))
            {
                Interlocked.Increment(ref count);
            }
        });
        listener.Start();

        _shared.Add("it", "Order.NotFound.Detail", "Ordine non trovato.");
        _shared.Add("it", "Type.NotFound.Title", "Non trovato");

        var text = Localizer().Localize(Order(), System.Globalization.CultureInfo.GetCultureInfo("it"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.IsFallback, Is.False);
            Assert.That(count, Is.Zero);
        }
    }
}
