using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sondor.Options.Tests;

[TestFixture]
public class SectionTests
{
    [Test]
    public void ARequiredSectionThatIsMissingFailsAndNamesTheSection()
    {
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>());

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Single(), Does.Contain("'DatabaseOptions'").And.Contain("required").And.Contain("missing"));
    }

    [Test]
    public void ARequiredSectionThatIsMissingDoesNotAlsoRunTheValidators()
    {
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<DatabaseOptions>().Validate(_ => false, "Never reached."));

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Single(), Does.Not.Contain("Never reached."));
    }

    [Test]
    public void ARequiredSectionWithAnEmptyValueCountsAsMissing()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions", null));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>());

        Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Test]
    public void AnOptionalSectionThatIsMissingIsNotConfiguredAndNothingFails()
    {
        var validated = 0;
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<FeatureOptions>(requirement: SectionRequirement.Optional)
                .Validate(_ => { validated++; return false; }, "Always fails when it runs."));

        var options = provider.GetRequiredService<IOptions<FeatureOptions>>().Value;

        Assert.That(options.Enabled, Is.False);
        Assert.That(validated, Is.Zero);
        Assert.That(provider.GetRequiredService<ISondorOptionsState<FeatureOptions>>().IsConfigured, Is.False);
    }

    [Test]
    public void AnOptionalSectionThatIsThereIsValidatedLikeARequiredOne()
    {
        var source = new ReloadableConfiguration().With(("FeatureOptions:Enabled", "true"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<FeatureOptions>(requirement: SectionRequirement.Optional)
                .Validate(options => options.Endpoint.Length > 0, "An endpoint is required when the feature is on."));

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<FeatureOptions>>().Value);

        Assert.That(exception.Failures.Single(), Is.EqualTo("An endpoint is required when the feature is on."));
        Assert.That(provider.GetRequiredService<ISondorOptionsState<FeatureOptions>>().IsConfigured, Is.True);
    }

    [Test]
    public void AnOptionalSectionThatIsThereAndValidBinds()
    {
        var source = new ReloadableConfiguration().With(("Features:Loki:Endpoint", "https://loki"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<FeatureOptions>("Features:Loki", SectionRequirement.Optional));

        Assert.That(provider.GetRequiredService<IOptions<FeatureOptions>>().Value.Endpoint, Is.EqualTo("https://loki"));
    }

    [Test]
    public void HasSondorSectionDecidesWhetherAFeatureIsRegistered()
    {
        var off = new ConfigurationBuilder().Add(new ReloadableConfiguration().With(("Unrelated", "x"))).Build();
        var on = new ConfigurationBuilder().Add(new ReloadableConfiguration().With(("FeatureOptions:Enabled", "true"))).Build();

        Assert.That(off.HasSondorSection<FeatureOptions>(), Is.False);
        Assert.That(on.HasSondorSection<FeatureOptions>(), Is.True);
        Assert.That(on.HasSondorSection<FeatureOptions>("Missing"), Is.False);
    }

    [Test]
    public async Task TheHostDoesNotStartWhenARequiredSectionIsMissing()
    {
        var builder = Hosting.Builder(new ReloadableConfiguration().With(("Unrelated", "x")));
        builder.Services.AddSondorOptions<DatabaseOptions>();
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(exception.Message, Does.Contain("'DatabaseOptions'"));
    }

    [Test]
    public async Task TheHostDoesNotStartWhenAnOptionalSectionIsThereButInvalid()
    {
        var builder = Hosting.Builder(new ReloadableConfiguration().With(("FeatureOptions:Enabled", "true")));
        builder.Services.AddSondorOptions<FeatureOptions>(requirement: SectionRequirement.Optional)
            .Validate(options => options.Endpoint.Length > 0, "An endpoint is required.");
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Test]
    public async Task TheHostStartsWhenAnOptionalSectionIsMissing()
    {
        var builder = Hosting.Builder(new ReloadableConfiguration().With(("Unrelated", "x")));
        builder.Services.AddSondorOptions<FeatureOptions>(requirement: SectionRequirement.Optional);
        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task TheHostStartsWhenEverythingIsValid()
    {
        var builder = Hosting.Builder(new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db")));
        builder.Services.AddSondorOptions<DatabaseOptions>().Validate(options => options.ConnectionString.Length > 0, "Required.");
        using var host = builder.Build();

        await host.StartAsync();

        Assert.That(host.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
        await host.StopAsync();
    }
}
