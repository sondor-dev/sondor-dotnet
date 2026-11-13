using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Options.FluentValidation.Tests;

[TestFixture]
public class FluentValidationTests
{
    private static ReloadableConfiguration Valid() =>
        new ReloadableConfiguration().With(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "10"));

    [Test]
    public void ValidOptionsPass()
    {
        using var provider = Hosting.Provider(Valid(), services =>
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>());

        Assert.That(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, Is.EqualTo("db"));
    }

    [Test]
    public void EveryFailingRuleIsReportedWithTheOptionsAndPropertyName()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "0"), ("DatabaseOptions:Pool:Size", "0"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>());

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Count(), Is.EqualTo(3));
        Assert.That(exception.Failures, Does.Contain("Validation failed for DatabaseOptions.ConnectionString with the error: A connection string is required."));
        Assert.That(exception.Failures, Does.Contain("Validation failed for DatabaseOptions.Timeout with the error: The timeout must be 1 to 300 seconds."));
        Assert.That(exception.Failures, Does.Contain("Validation failed for DatabaseOptions.Pool.Size with the error: The pool needs at least one connection."));
    }

    [Test]
    public void AFailureOfTheWholeObjectHasNoPropertyName()
    {
        using var provider = Hosting.Provider(Valid(), services =>
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, ModelLevelValidator>());

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Single(), Is.EqualTo("Validation failed for DatabaseOptions with the error: The whole thing is wrong."));
    }

    [Test]
    public void AValidatorRegisteredByTheCallerIsUsed()
    {
        using var provider = Hosting.Provider(Valid(), services =>
        {
            services.AddScoped<IValidator<DatabaseOptions>, DatabaseOptionsValidator>();
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently();
        });

        Assert.That(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Timeout, Is.EqualTo(10));
    }

    [Test]
    public void AValidatorAlreadyRegisteredIsNotReplaced()
    {
        using var provider = Hosting.Provider(Valid(), services =>
        {
            services.AddScoped<IValidator<DatabaseOptions>, CountingValidator>();
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>();
        });

        Assert.That(provider.CreateScope().ServiceProvider.GetRequiredService<IValidator<DatabaseOptions>>(), Is.TypeOf<CountingValidator>());
    }

    [Test]
    public void WithoutAValidatorTheFailureSaysSo()
    {
        using var provider = Hosting.Provider(Valid(), services => services.AddSondorOptions<DatabaseOptions>().ValidateFluently());

        var exception = Assert.Throws<InvalidOperationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Message, Does.Contain("IValidator<DatabaseOptions>").And.Contain("ValidateFluently"));
    }

    [Test]
    public void AValidatorCanDependOnAScopedServiceAndEachValidationHasItsOwnScope()
    {
        ScopedDependencyValidator.Seen.Clear();
        var source = Valid();
        using var provider = Hosting.Provider(source, services =>
        {
            services.AddScoped<ScopeMarker>();
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, ScopedDependencyValidator>();
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        source.Replace(("DatabaseOptions:ConnectionString", "db2"));
        _ = monitor.CurrentValue;

        Assert.That(ScopedDependencyValidator.Seen, Has.Count.EqualTo(2));
        Assert.That(ScopedDependencyValidator.Seen.Distinct().Count(), Is.EqualTo(2), "a scope for each validation");
    }

    [Test]
    public void AnOptionalSectionThatIsNotConfiguredDoesNotRunTheValidator()
    {
        CountingValidator.Runs = 0;
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<DatabaseOptions>(requirement: SectionRequirement.Optional)
                .ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>());

        Assert.DoesNotThrow(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Test]
    public void AnOptionalSectionThatIsThereIsValidated()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Hosting.Provider(source, services =>
            services.AddSondorOptions<DatabaseOptions>(requirement: SectionRequirement.Optional)
                .ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>());

        var exception = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.That(exception.Failures.Single(), Does.Contain("ConnectionString"));
    }

    [Test]
    public void TheValidatorRunsOncePerBuildAndNotForOtherNames()
    {
        CountingValidator.Runs = 0;
        using var provider = Hosting.Provider(Valid(), services =>
        {
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, CountingValidator>();
            services.Configure<DatabaseOptions>("other", options => options.Timeout = 5);
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();

        _ = monitor.CurrentValue;
        _ = monitor.Get("other");

        Assert.That(CountingValidator.Runs, Is.EqualTo(1));
    }

    [Test]
    public void OnTheFrameworksOwnBuilderItValidatesOnlyTheNameTheBuilderWasMadeFor()
    {
        using var provider = Hosting.Provider(Valid(), services =>
        {
            services.AddOptions<DatabaseOptions>("named").Configure(options => options.Timeout = 0)
                .ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>();
            services.AddOptions<DatabaseOptions>().Configure(options => options.Timeout = 0);
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();

        Assert.DoesNotThrow(() => _ = monitor.CurrentValue, "the default name has no validator");
        Assert.Throws<OptionsValidationException>(() => _ = monitor.Get("named"));
    }

    [Test]
    public void AnAsynchronousRuleFailsTheStartBecauseOptionsAreBuiltSynchronously()
    {
        using var provider = Hosting.Provider(Valid(), services =>
            services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, AsyncValidator>());

        var exception = Assert.Catch<InvalidOperationException>(() => _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
        Assert.That(exception.GetType().Name, Is.EqualTo("AsyncValidatorInvokedSynchronouslyException"));
    }

    [Test]
    public async Task TheHostDoesNotStartWithInvalidOptions()
    {
        var builder = Hosting.Builder(new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "0")));
        builder.Services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>();
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(exception.Message, Does.Contain("DatabaseOptions.Timeout"));
    }

    [Test]
    public void AnInvalidReloadIsRejectedAndTheReasonIsLogged()
    {
        var logs = new CapturedLogs();
        var source = Valid();
        using var provider = Hosting.Provider(
            source,
            services => services.AddSondorOptions<DatabaseOptions>().ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>(),
            logs);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        Assert.DoesNotThrow(() => source.Replace(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "9999")));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));
        Assert.That(logs.Entries.Single(entry => entry.Level == LogLevel.Warning).Message, Does.Contain("The timeout must be 1 to 300 seconds."));

        source.Replace(("DatabaseOptions:ConnectionString", "db"), ("DatabaseOptions:Timeout", "20"));
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(20));
    }

    [Test]
    public void ArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => FluentValidationOptionsExtensions.ValidateFluently<DatabaseOptions>(null!));
        Assert.Throws<ArgumentNullException>(() => FluentValidationOptionsExtensions.ValidateFluently<DatabaseOptions, DatabaseOptionsValidator>(null!));
    }
}
