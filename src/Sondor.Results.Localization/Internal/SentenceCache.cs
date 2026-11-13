using System.Collections.Concurrent;

namespace Sondor.Results.Localization.Internal;

/// <summary>
/// The parsed form of each translated sentence, so a sentence is parsed once however many errors use it. A sentence that does not
/// parse is remembered too, so a bad translation costs one exception, not one for every request. The cache stops growing at a fixed
/// size: the sentences are the application's own resources, so it never gets near it, but a source that makes up sentences cannot grow
/// memory without limit.
/// </summary>
internal sealed class SentenceCache
{
    private const int Capacity = 1024;

    // Errors are made from a sentence by an ErrorTemplate, which owns the placeholder syntax and the rules for showing data, so the
    // text of a translation is rendered exactly as the English is. The code and type are not used: only the description is read.
    private const string TemplateCode = "Localized.Sentence";

    private readonly ConcurrentDictionary<(string Sentence, bool ShowData), Entry> _entries = new();

    public ErrorTemplate? Get(string sentence, bool showData, out string? reason)
    {
        if (_entries.TryGetValue((sentence, showData), out var known))
        {
            reason = known.Reason;

            return known.Template;
        }

        Entry entry;

        try
        {
            entry = new Entry(new ErrorTemplate(TemplateCode, ErrorType.Failure, sentence, showData), null);
        }
        catch (ArgumentException exception)
        {
            entry = new Entry(null, exception.Message);
        }

        if (_entries.Count < Capacity)
        {
            _entries.TryAdd((sentence, showData), entry);
        }

        reason = entry.Reason;

        return entry.Template;
    }

    private sealed record Entry(ErrorTemplate? Template, string? Reason);
}
