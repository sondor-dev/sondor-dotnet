using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sondor.Results.Localization.Tests.Resources;

namespace Sondor.Results.Localization.Tests;

[TestFixture]
public sealed class RegistrationTests
{
    [Test]
    public void Adding_The_Localization_With_No_Settings_Should_Work_And_Keep_The_English_Text()
    {
        var services = new ServiceCollection().AddErrorLocalization();

        var text = services.BuildServiceProvider().GetRequiredService<IErrorLocalizer>()
            .Localize(Error.NotFound("Order", "Id"), Setup.French);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(text.IsFallback, Is.True, "nothing is translated, and that is visible");
        }
    }

    [Test]
    public void One_Localizer_Should_Be_Shared_And_Adding_It_Twice_Should_Keep_Both_Configurations()
    {
        var factory = new FakeLocalizerFactory();
        factory.Resource<OrderResource>().Add("fr", "Order.NotFound.Detail", "Commande introuvable.");
        factory.Resource<SharedResource>().Add("fr", "Resource.NotFound.Detail", "Introuvable.");

        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizerFactory>(factory);
        services.AddErrorLocalization(options => options.UseResource<OrderResource>("Order"));
        services.AddErrorLocalization(options => options.UseResource<SharedResource>());

        using var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IErrorLocalizer>();
        var specific = ErrorTemplates.NotFound.WithCode("Order.NotFound")
            .Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredService<IErrorLocalizer>(), Is.SameAs(localizer));
            Assert.That(localizer.Localize(specific, Setup.French).Detail, Is.EqualTo("Commande introuvable."));
            Assert.That(localizer.Localize(Error.NotFound("Customer", "Id"), Setup.French).Detail, Is.EqualTo("Introuvable."));
        }
    }

    [Test]
    public void A_Resource_Without_A_Localizer_Factory_Should_Fail_Clearly_When_The_Localizer_Is_Resolved()
    {
        var provider = new ServiceCollection()
            .AddErrorLocalization(options => options.UseResource<OrderResource>())
            .BuildServiceProvider();

        Assert.That(() => provider.GetRequiredService<IErrorLocalizer>(),
            Throws.InstanceOf<OptionsValidationException>().With.Message.Contains("AddLocalization"));
    }

    [Test]
    public async Task A_Resource_Without_A_Localizer_Factory_Should_Stop_The_Host_From_Starting()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddErrorLocalization(options => options.UseResource<OrderResource>());

        using var host = builder.Build();

        Assert.That(async () => await host.StartAsync(),
            Throws.InstanceOf<OptionsValidationException>().With.Message.Contains("IStringLocalizerFactory"));

        await Task.CompletedTask;
    }

    [Test]
    public void Options_That_Cannot_Work_Should_Be_Refused_With_The_Reason()
    {
        var factory = new FakeLocalizerFactory();

        var provider = new ServiceCollection()
            .AddSingleton<IStringLocalizerFactory>(factory)
            .AddErrorLocalization(options =>
            {
                options.KeyPrefix = null!;
                options.SourceCulture = "  ";
                options.MissingTranslationLevel = (LogLevel)42;
                options.ShowDataFor.Add(" ");
            })
            .BuildServiceProvider();

        var validation = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<ErrorLocalizationOptions>>().Value);

        Assert.That(validation.Failures, Has.Some.Contain("KeyPrefix"));
        Assert.That(validation.Failures, Has.Some.Contain("SourceCulture"));
        Assert.That(validation.Failures, Has.Some.Contain("MissingTranslationLevel"));
        Assert.That(validation.Failures, Has.Some.Contain("ShowDataFor"));
    }

    [Test]
    public void The_Services_Should_Be_Given()
    {
        Assert.That(() => ErrorLocalizationServiceCollectionExtensions.AddErrorLocalization(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void The_Resx_Files_Of_An_Application_Should_Be_Used_Through_The_Standard_Localization()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .AddErrorLocalization(options => options.UseResource<TestResource>())
            .BuildServiceProvider();

        var localizer = provider.GetRequiredService<IErrorLocalizer>();

        var french = localizer.Localize(Error.NotFound("Order", "Id"), Setup.French);
        var canadian = localizer.Localize(Error.NotFound("Order", "Id"), System.Globalization.CultureInfo.GetCultureInfo("fr-CA"));
        var german = localizer.Localize(Error.NotFound("Order", "Id"), Setup.German);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(french.Title, Is.EqualTo("Introuvable"));
            Assert.That(french.Detail, Is.EqualTo("Aucune commande n'a été trouvée pour l'identifiant."));
            Assert.That(french.IsFallback, Is.False);
            Assert.That(canadian.Detail, Is.EqualTo(french.Detail), "a regional culture reads its language's resource");
            Assert.That(german.Detail, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(german.IsFallback, Is.True);
        }
    }

    [Test]
    public void The_Built_In_Errors_Should_Be_Translated_End_To_End_From_The_Resx_Files()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .AddErrorLocalization(options => options.UseResource<TestResource>())
            .BuildServiceProvider();

        var localizer = provider.GetRequiredService<IErrorLocalizer>();

        var text = localizer.Localize(Error.Unauthorized("view invoices"), Setup.French);

        Assert.That(text.Detail, Is.EqualTo("Connectez-vous pour consulter les factures."));
    }
}
