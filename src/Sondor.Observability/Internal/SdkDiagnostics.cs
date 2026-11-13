using System.Diagnostics.Tracing;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Sondor.Observability.Internal;

/// <summary>The logger category the SDK's own diagnostics are written to. Kept out of the OpenTelemetry log exporter.</summary>
internal static class DiagnosticsCategory
{
    public const string Name = "Sondor.Observability.Diagnostics";
}

/// <summary>
/// Forwards what the OpenTelemetry SDK reports about itself (it only ever does so through event sources) to a logger. It exists
/// only while the level asks for something, so a service that turned this off pays nothing for it.
/// </summary>
internal sealed partial class SdkDiagnostics : EventListener
{
    private readonly ILogger _logger;
    private readonly EventLevel _level;
    private readonly List<EventSource> _created = [];
    private volatile bool _enabling = true;

    public SdkDiagnostics(ILogger logger, LogLevel level)
    {
        _logger = logger;
        _level = ToEventLevel(level);

        // Event sources that exist already are announced from inside the base constructor, before this one has run: they were
        // collected then, and are switched on now.
        lock (_created)
        {
            foreach (var source in _created)
            {
                EnableEvents(source, _level);
            }

            _enabling = false;
        }
    }

    public static EventLevel ToEventLevel(LogLevel level) => level switch
    {
        LogLevel.Critical or LogLevel.Error => EventLevel.Error,
        LogLevel.Warning => EventLevel.Warning,
        LogLevel.Information => EventLevel.Informational,
        _ => EventLevel.Verbose,
    };

    public static LogLevel ToLogLevel(EventLevel level) => level switch
    {
        EventLevel.Critical or EventLevel.LogAlways => LogLevel.Critical,
        EventLevel.Error => LogLevel.Error,
        EventLevel.Warning => LogLevel.Warning,
        EventLevel.Informational => LogLevel.Information,
        _ => LogLevel.Debug,
    };

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (!eventSource.Name.StartsWith("OpenTelemetry", StringComparison.Ordinal))
        {
            return;
        }

        // Events can be written from the thread that creates the source, and from any other.
        bool enableNow;

        lock (_created)
        {
            _created.Add(eventSource);
            enableNow = !_enabling;
        }

        if (enableNow)
        {
            EnableEvents(eventSource, _level);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        var level = ToLogLevel(eventData.Level);

        if (_logger.IsEnabled(level))
        {
            var text = Text(eventData);

            Forward(_logger, level, eventData.EventSource.Name, text);
        }
    }

    private static string Text(EventWrittenEventArgs eventData)
    {
        if (string.IsNullOrEmpty(eventData.Message))
        {
            return eventData.EventName ?? "(no message)";
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, eventData.Message, [.. eventData.Payload ?? []]);
        }
        catch (FormatException)
        {
            return eventData.Message;
        }
    }

    [LoggerMessage(EventId = 1, Message = "{Source}: {Text}")]
    private static partial void Forward(ILogger logger, LogLevel level, string source, string text);
}
