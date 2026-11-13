using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sondor.Options.Tests;

/// <summary>
/// Configuration that changes while the service runs, which is what an Azure App Configuration refresh does when its
/// sentinel key changes: the provider replaces its data and announces a reload.
/// </summary>
[TestFixture]
public class ReloadTests
{
    private static ServiceProvider Start(
        ReloadableConfiguration source,
        CapturedLogs? logs = null,
        InvalidReloadBehavior behavior = InvalidReloadBehavior.KeepLastValid,
        SectionRequirement requirement = SectionRequirement.Required) =>
        Hosting.Provider(
            source,
            services => services.AddSondorOptions<DatabaseOptions>(requirement: requirement, onInvalidReload: behavior)
                .Validate(options => options.Timeout > 0, "Timeout must be positive."),
            logs);

    [Test]
    public void TheMonitorFollowsAChangeAndTellsItsListeners()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        var seen = new List<int>();
        using var subscription = monitor.OnChange(options => seen.Add(options.Timeout));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));

        source.Replace(("DatabaseOptions:Timeout", "20"));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(20));
        Assert.That(seen.Single(), Is.EqualTo(20));
    }

    [Test]
    public void ASnapshotSeesTheChangeInTheNextScopeButNotInTheCurrentOne()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source);

        using var first = provider.CreateScope();
        var firstSnapshot = first.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>();
        Assert.That(firstSnapshot.Value.Timeout, Is.EqualTo(10));

        source.Replace(("DatabaseOptions:Timeout", "20"));

        using var second = provider.CreateScope();
        Assert.That(firstSnapshot.Value.Timeout, Is.EqualTo(10));
        Assert.That(second.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout, Is.EqualTo(20));
    }

    [Test]
    public void IOptionsKeepsTheValueItStartedWith()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source);
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>();
        Assert.That(options.Value.Timeout, Is.EqualTo(10));

        source.Replace(("DatabaseOptions:Timeout", "20"));

        Assert.That(options.Value.Timeout, Is.EqualTo(10));
    }

    [Test]
    public void AnInvalidChangeIsRejectedAndTheLastValidOptionsStayInUse()
    {
        var logs = new CapturedLogs();
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, logs);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));

        Assert.DoesNotThrow(() => source.Replace(("DatabaseOptions:Timeout", "-5")));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));
        var warning = logs.Entries.Single(entry => entry.Level == LogLevel.Warning).Message;
        Assert.That(warning, Does.Contain("DatabaseOptions").And.Contain("rejected").And.Contain("Timeout must be positive."));
    }

    [Test]
    public void AValidChangeAfterARejectedOneApplies()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, new CapturedLogs());
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        source.Replace(("DatabaseOptions:Timeout", "-5"));
        source.Replace(("DatabaseOptions:Timeout", "30"));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(30));
    }

    [Test]
    public void AValueThatCannotBeBoundIsRejectedToo()
    {
        var logs = new CapturedLogs();
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, logs);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        Assert.DoesNotThrow(() => source.Replace(("DatabaseOptions:Timeout", "ten")));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));
        Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1));
    }

    [Test]
    public void ASnapshotInANewScopeAlsoGetsTheLastValidOptionsAfterARejectedChange()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, new CapturedLogs());
        _ = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue;

        source.Replace(("DatabaseOptions:Timeout", "-5"));

        using var scope = provider.CreateScope();
        Assert.That(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<DatabaseOptions>>().Value.Timeout, Is.EqualTo(10));
    }

    [Test]
    public void ARequiredSectionThatDisappearsOnReloadIsRejected()
    {
        var logs = new CapturedLogs();
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, logs);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        source.Replace(("Unrelated", "x"));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(10));
        Assert.That(logs.Entries.Single(entry => entry.Level == LogLevel.Warning).Message, Does.Contain("is required"));
    }

    [Test]
    public void AnOptionalFeatureSwitchesOnAndOffWithTheConfiguration()
    {
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Start(source, requirement: SectionRequirement.Optional);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        var state = provider.GetRequiredService<ISondorOptionsState<DatabaseOptions>>();

        Assert.That(state.IsConfigured, Is.False);

        source.Replace(("DatabaseOptions:Timeout", "15"));
        Assert.That(state.IsConfigured, Is.True);
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(15));

        source.Replace(("Unrelated", "x"));
        Assert.That(state.IsConfigured, Is.False);
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(30), "back to the defaults");
    }

    [Test]
    public void SwitchingAnOptionalFeatureOnWithAnInvalidValueIsRejected()
    {
        var logs = new CapturedLogs();
        var source = new ReloadableConfiguration().With(("Unrelated", "x"));
        using var provider = Start(source, logs, requirement: SectionRequirement.Optional);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        source.Replace(("DatabaseOptions:Timeout", "-1"));

        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(30), "the feature stays off");
        Assert.That(logs.Entries.Count(entry => entry.Level == LogLevel.Warning), Is.EqualTo(1));
    }

    [Test]
    public void WithFailTheFrameworksBehaviourIsKept()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "10"));
        using var provider = Start(source, behavior: InvalidReloadBehavior.Fail);
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        _ = monitor.CurrentValue;

        Assert.Throws<AggregateException>(() => source.Replace(("DatabaseOptions:Timeout", "-5")));
        Assert.Throws<OptionsValidationException>(() => _ = monitor.CurrentValue);

        source.Replace(("DatabaseOptions:Timeout", "20"));
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(20));
    }

    [Test]
    public void TheStartIsStrictWhateverTheReloadBehaviour()
    {
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "-5"));
        using var provider = Start(source, new CapturedLogs());

        Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>().CurrentValue);
    }

    [Test]
    public void ManyReloadsBuildTheOptionsOncePerChangeAndNeverMore()
    {
        const int Reloads = 500;
        var builds = 0;
        var source = new ReloadableConfiguration().With(("DatabaseOptions:Timeout", "1"));
        using var provider = Hosting.Provider(source, services =>
        {
            services.AddSondorOptions<DatabaseOptions>().Validate(_ => true);
            services.AddSingleton<IConfigureOptions<DatabaseOptions>>(new ConfigureOptions<DatabaseOptions>(_ => builds++));
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<DatabaseOptions>>();
        var notifications = 0;
        using var subscription = monitor.OnChange(_ => notifications++);
        _ = monitor.CurrentValue;
        var baseline = builds;

        for (var reload = 2; reload < Reloads + 2; reload++)
        {
            source.Replace(("DatabaseOptions:Timeout", reload.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            _ = monitor.CurrentValue;
        }

        Assert.That(builds - baseline, Is.EqualTo(Reloads), "one build per change");
        Assert.That(notifications, Is.EqualTo(Reloads), "one notification per change");
        Assert.That(monitor.CurrentValue.Timeout, Is.EqualTo(Reloads + 1));
    }
}
