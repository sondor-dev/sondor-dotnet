using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Sondor.Results.Localization.Tests;

// Marker types, one for each resource a test sets up.
internal sealed class OrderResource;

internal sealed class BillingResource;

internal sealed class SharedResource;

/// <summary>The text of one resource, by culture and key, as a translation store would hold it.</summary>
internal sealed class FakeResource
{
    private readonly Dictionary<(string Culture, string Key), string> _entries = [];

    public List<string> Lookups { get; } = [];

    public FakeResource Add(string culture, string key, string value)
    {
        _entries[(culture, key)] = value;

        return this;
    }

    /// <summary>What a resource manager does: the culture asked for, then each parent culture, then the neutral resource.</summary>
    public string? Find(string key)
    {
        lock (Lookups)
        {
            Lookups.Add(key);
        }

        for (var culture = CultureInfo.CurrentUICulture; ; culture = culture.Parent)
        {
            if (_entries.TryGetValue((culture.Name, key), out var value))
            {
                return value;
            }

            if (culture.Name.Length == 0)
            {
                return null;
            }
        }
    }
}

internal sealed class FakeLocalizer(FakeResource resource) : IStringLocalizer
{
    public LocalizedString this[string name] =>
        resource.Find(name) is { } value ? new LocalizedString(name, value) : new LocalizedString(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] => this[name];

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal sealed class FakeLocalizerFactory : IStringLocalizerFactory
{
    private readonly Dictionary<string, FakeResource> _resources = [];

    public FakeResource Resource<T>() => Resource(typeof(T).FullName!);

    public FakeResource Resource(string name)
    {
        if (!_resources.TryGetValue(name, out var resource))
        {
            _resources[name] = resource = new FakeResource();
        }

        return resource;
    }

    public IStringLocalizer Create(Type resourceSource) => new FakeLocalizer(Resource(resourceSource.FullName!));

    public IStringLocalizer Create(string baseName, string location) => new FakeLocalizer(Resource(baseName));
}

internal sealed record LogEntry(LogLevel Level, int EventId, string Message);

/// <summary>Collects what the localizer logs, so a test can say what was reported and how often.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory, ILogger
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, eventId.Id, formatter(state, exception)));
        }
    }

    public void Dispose()
    {
    }
}

internal static class Setup
{
    public static IErrorLocalizer Localizer(
        FakeLocalizerFactory factory,
        CapturingLoggerFactory? logs = null,
        Action<ErrorLocalizationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStringLocalizerFactory>(factory);
        services.AddSingleton<ILoggerFactory>(logs ?? new CapturingLoggerFactory());
        services.AddErrorLocalization(configure);

        return services.BuildServiceProvider().GetRequiredService<IErrorLocalizer>();
    }

    public static CultureInfo French { get; } = CultureInfo.GetCultureInfo("fr");

    public static CultureInfo German { get; } = CultureInfo.GetCultureInfo("de");
}
