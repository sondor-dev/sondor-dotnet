using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Sondor.Results.Localization.Internal;

/// <summary>
/// Makes a missing translation visible without changing what the reader sees: a counter every time, and one log entry for each
/// culture and key, so a busy service does not flood its log. Also reports a translation that exists but cannot be used.
/// </summary>
internal sealed partial class MissingTranslations(ILogger logger, LogLevel level)
{
    /// <summary>The name of the meter, for an OpenTelemetry <c>AddMeter</c>.</summary>
    internal const string MeterName = "Sondor.Results.Localization";

    // The report stops growing at this many distinct (culture, key) pairs: a caller that keeps inventing cultures cannot grow memory.
    private const int Capacity = 4096;

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Counter = Meter.CreateCounter<long>(
        "sondor.results.localization.missing_translations",
        unit: "{translation}",
        description: "Errors shown in English because the reader's culture has no translation for them.");

    private readonly ConcurrentDictionary<(string Culture, string Key, Kind Kind), byte> _reported = new();

    private enum Kind
    {
        Missing,
        Unusable,
        Vocabulary
    }

    /// <summary>No translation of a title or a message was found. The tags are the culture and the area, never a value.</summary>
    public void Missing(string culture, string key, string? area)
    {
        if (Counter.Enabled)
        {
            var tags = new TagList { { "culture", culture }, { "area", area ?? string.Empty } };
            Counter.Add(1, tags);
        }

        if (level != LogLevel.None && FirstTime(culture, key, Kind.Missing))
        {
            NoTranslation(logger, level, key, culture);
        }
    }

    /// <summary>A translation was found but cannot be used (it does not parse, or it names something the error does not have).</summary>
    public void Unusable(string culture, string key, string reason)
    {
        if (FirstTime(culture, key, Kind.Unusable))
        {
            UnusableTranslation(logger, key, culture, reason);
        }
    }

    /// <summary>A word of the vocabulary has no translation, so it stays as written. Often deliberate, so it is only for debugging.</summary>
    public void Vocabulary(string culture, string key)
    {
        if (logger.IsEnabled(LogLevel.Debug) && FirstTime(culture, key, Kind.Vocabulary))
        {
            NoVocabulary(logger, key, culture);
        }
    }

    private bool FirstTime(string culture, string key, Kind kind) =>
        _reported.Count < Capacity && _reported.TryAdd((culture, key, kind), 0);

    [LoggerMessage(EventId = 1, Message = "No translation for the key '{Key}' in the culture '{Culture}'; the English text is used.")]
    private static partial void NoTranslation(ILogger logger, LogLevel level, string key, string culture);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "The translation for the key '{Key}' in the culture '{Culture}' cannot be used and the next layer is used instead: {Reason}")]
    private static partial void UnusableTranslation(ILogger logger, string key, string culture, string reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "No translation for the word '{Key}' in the culture '{Culture}'; it is used as written.")]
    private static partial void NoVocabulary(ILogger logger, string key, string culture);
}
