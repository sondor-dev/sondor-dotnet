using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sondor.Options.Tests;

[TestFixture]
public class BindingTests
{
    [Test]
    public void TheSectionIsNamedAfterTheTypeByDefault()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "5"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>());

        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.That(options.ConnectionString, Is.EqualTo("db"));
        Assert.That(options.Timeout, Is.EqualTo(5));
    }

    [TestCase("Database")]
    [TestCase("Services:Database")]
    public void AnotherSectionOrANestedPathCanBeNamed(string section)
    {
        var source = new ReloadableConfiguration().With(($"{section}:ConnectionString", "db"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>(section));

        Assert.That(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankSectionMeansTheTypeName(string? section)
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>(section));

        Assert.That(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
    }

    [Test]
    public void EveryFlavourOfIOptionsIsAvailable()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>());
        using var scope = provider.CreateScope();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
            Assert.That(provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue.ConnectionString, Is.EqualTo("db"));
            Assert.That(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
            Assert.That(provider.GetRequiredService<IOptionsFactory<DatabaseOptions>>().Create(Microsoft.Extensions.Options.Options.DefaultName).ConnectionString, Is.EqualTo("db"));
        });
    }

    [Test]
    public void ImmutableShapesAndCollectionsBind()
    {
        var source = new ReloadableConfiguration().With(("ImmutableOptions:Name", "node"), ("ImmutableOptions:Tags:0", "a"), ("ImmutableOptions:Tags:1", "b"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<ImmutableOptions>());

        var options = provider.GetRequiredService<IOptions<ImmutableOptions>>().Value;

        Assert.That(options.Name, Is.EqualTo("node"));
        Assert.That(string.Join(",", options.Tags), Is.EqualTo("a,b"));
    }

    [Test]
    public void ValidationAddedToTheBuilderIsApplied()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "0"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<DatabaseOptions>().Validate(options => options.Timeout > 0, "Timeout must be positive."));

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Single(), Is.EqualTo("Timeout must be positive."));
    }

    [Test]
    public void NamedOptionsOfTheSameTypeAreNotBoundToTheSection()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db"));
        using var provider = Hosting.Provider(source, services =>
        {
            services.AddSondorOptions<DatabaseOptions>();
            services.Configure<DatabaseOptions>("other", options => options.ConnectionString = "named");
        });

        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();

        Assert.That(monitor.Get("other").ConnectionString, Is.EqualTo("named"));
        Assert.That(monitor.CurrentValue.ConnectionString, Is.EqualTo("db"));
    }

    [Test]
    public void ABadValueFailsAtStartup()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "not a number"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>());

        Assert.Throws<InvalidOperationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Test]
    public void CallingItAgainForTheSameTypeLetsTheLastCallDecide()
    {
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Hosting.Provider(source, services =>
        {
            services.AddSondorOptions<FeatureOptions>();
            services.AddSondorOptions<FeatureOptions>(requirement: SectionRequirement.Optional);
        });

        Assert.DoesNotThrow(() => _ = provider.GetRequiredService<IOptions<FeatureOptions>>().Value);
        Assert.That(provider.GetServices<ISondorOptionsState<FeatureOptions>>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => SondorOptionsExtensions.AddSondorOptions<DatabaseOptions>(null!));
        Assert.Throws<ArgumentNullException>(() => SondorOptionsExtensions.HasSondorSection<DatabaseOptions>(null!));

        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddSondorOptions<DatabaseOptions>(requirement: (SectionRequirement)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddSondorOptions<DatabaseOptions>(onInvalidReload: (InvalidReloadBehavior)42));
    }

    [Test]
    public void TheSectionStateReadsTheSameSectionAsTheBinding()
    {
        var source = new ReloadableConfiguration().With(("Database:ConnectionString", "db"));
        using var provider = Hosting.Provider(source, services => services.AddSondorOptions<DatabaseOptions>("Database"));

        Assert.That(provider.GetRequiredService<ISondorOptionsState<DatabaseOptions>>().IsConfigured, Is.True);
        Assert.That(new ConfigurationBuilder().Add(source).Build().HasSondorSection<DatabaseOptions>("Database"), Is.True);
        Assert.That(new ConfigurationBuilder().Add(source).Build().HasSondorSection<DatabaseOptions>(), Is.False);
    }
}
