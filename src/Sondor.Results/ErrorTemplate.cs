using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Sondor.Results;

/// <summary>
/// A reusable way to build an <see cref="Error"/>: a stable code, an <see cref="ErrorType"/> and an English sentence with named
/// placeholders. Declare it once as a <see langword="static"/> <see langword="readonly"/> field and call
/// <see cref="Create"/> wherever the failure happens.
/// </summary>
/// <remarks>
/// <para>
/// The sentence is only the English fallback. A layer that translates looks the error up by its code (or by
/// <see cref="ErrorArguments.Template"/>) and fills its own sentence with the same arguments.
/// </para>
/// <para>
/// A template is immutable and thread-safe. The sentence is parsed once, when the template is made. Placeholders are plain
/// names in braces, <c>{Entity}</c>; there are no positions (<c>{0}</c>), formats (<c>{Name:N2}</c>) or alignments, so nothing
/// depends on the current culture. <c>{{</c> and <c>}}</c> are literal braces. A sentence has at most 16 placeholders.
/// </para>
/// </remarks>
public sealed class ErrorTemplate
{
    private const int MaximumPlaceholders = 16;
    private const int MaximumDisplayedDataLength = 64;

    private readonly string[] _literals;
    private readonly string[] _names;
    private readonly int _literalsLength;
    private readonly string _rootCode;
    private readonly ErrorArguments? _withoutPlaceholders;

    /// <summary>Makes a template.</summary>
    /// <param name="code">The stable <c>Area.Reason</c> code of the errors it creates. Never built from request data.</param>
    /// <param name="type">The kind of failure.</param>
    /// <param name="format">The English sentence, with <c>{Name}</c> placeholders.</param>
    /// <param name="showData">
    /// Whether the sentence may name a <see cref="ErrorArgumentKind.Data"/> argument. Off by default, because a value from a
    /// caller reflected into a message is an injection and information-leak risk. When on, the value is shown with control
    /// characters replaced and at most 64 characters.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The code is blank, or the format is not valid: an unclosed or unmatched brace, a placeholder that is not a plain name
    /// (<c>{0}</c>, <c>{Name:N2}</c>, <c>{}</c>), the same placeholder twice, or more than 16 placeholders.
    /// </exception>
    public ErrorTemplate(string code, ErrorType type, string format, bool showData = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(format);

        (_literals, _names) = Parse(format);

        Code = code;
        _rootCode = code;
        Type = type;
        Format = format;
        ShowData = showData;
        Placeholders = Array.AsReadOnly(_names);
        _literalsLength = _literals.Sum(static literal => literal.Length);
        _withoutPlaceholders = _names.Length == 0 ? new ErrorArguments([], _rootCode, _literals[0]) : null;
    }

    private ErrorTemplate(ErrorTemplate source, string code)
    {
        _literals = source._literals;
        _names = source._names;
        _literalsLength = source._literalsLength;
        _rootCode = source._rootCode;
        Code = code;
        Type = source.Type;
        Format = source.Format;
        ShowData = source.ShowData;
        Placeholders = source.Placeholders;
        _withoutPlaceholders = source._withoutPlaceholders;
    }

    /// <summary>The code of the errors this template creates.</summary>
    public string Code { get; }

    /// <summary>The kind of failure.</summary>
    public ErrorType Type { get; }

    /// <summary>The English sentence, as it was declared.</summary>
    public string Format { get; }

    /// <summary>The placeholder names in the sentence, in order of appearance.</summary>
    public IReadOnlyList<string> Placeholders { get; }

    /// <summary>Whether the sentence may name a <see cref="ErrorArgumentKind.Data"/> argument.</summary>
    public bool ShowData { get; }

    /// <summary>
    /// The same sentence, type and placeholders under a code of your own, for a failure clients should be able to tell apart:
    /// <c>ErrorTemplates.NotFound.WithCode("Order.NotFound")</c>. The errors it creates still report the original template in
    /// <see cref="ErrorArguments.Template"/>, so a layer with no wording for the specific code can fall back to the general one.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null or blank.</exception>
    public ErrorTemplate WithCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new ErrorTemplate(this, code);
    }

    /// <summary>Creates the error: the arguments are kept and the description is the sentence with them filled in.</summary>
    /// <param name="arguments">One argument for every placeholder, and optionally more <c>Data</c> or <c>Sensitive</c> context.</param>
    /// <exception cref="ArgumentException">
    /// A placeholder has no argument, a name is given twice, a <see cref="ErrorArgumentKind.Term"/> names no placeholder, a
    /// <see cref="ErrorArgumentKind.Data"/> argument fills a placeholder of a template that does not show data, or an argument
    /// is <c>default</c>. These are mistakes in the code that creates the error, so they are not tolerated at run time.
    /// </exception>
    public Error Create(params ReadOnlySpan<ErrorArgument> arguments)
    {
        // Which argument fills each placeholder, found once while the arguments are checked.
        Span<int> map = stackalloc int[MaximumPlaceholders];

        Validate(arguments, map[.._names.Length]);

        var detail = arguments.IsEmpty && _withoutPlaceholders is not null
            ? _withoutPlaceholders
            : new ErrorArguments(arguments.ToArray(), _rootCode, Describe(arguments, map[.._names.Length]));

        return new Error(Code, detail, Type);
    }

    /// <summary>The code.</summary>
    public override string ToString() => Code;

    private string Describe(ReadOnlySpan<ErrorArgument> arguments, ReadOnlySpan<int> map)
    {
        if (_names.Length == 0)
        {
            return _literals[0];
        }

        // The usual case, every placeholder a term: one allocation, the finished string.
        var length = _literalsLength;

        foreach (var index in map)
        {
            if (arguments[index].Kind != ErrorArgumentKind.Term)
            {
                return DescribeWithData(arguments, map);
            }

            length += ((string)arguments[index].Value!).Length;
        }

        return string.Create(length, new TermState(_literals, arguments, map), static (destination, state) =>
        {
            var position = 0;

            for (var i = 0; i < state.Map.Length; i++)
            {
                position = Copy(state.Literals[i], destination, position);
                position = Copy((string)state.Arguments[state.Map[i]].Value!, destination, position);
            }

            Copy(state.Literals[^1], destination, position);
        });
    }

    private static int Copy(string text, Span<char> destination, int position)
    {
        text.CopyTo(destination[position..]);

        return position + text.Length;
    }

    // A placeholder that is sensitive, or data a template has chosen to show.
    private string DescribeWithData(ReadOnlySpan<ErrorArgument> arguments, ReadOnlySpan<int> map)
    {
        var handler = new DefaultInterpolatedStringHandler(Format.Length, _names.Length, CultureInfo.InvariantCulture);

        for (var i = 0; i < map.Length; i++)
        {
            handler.AppendLiteral(_literals[i]);
            AppendArgument(ref handler, arguments[map[i]]);
        }

        handler.AppendLiteral(_literals[^1]);

        return handler.ToStringAndClear();
    }

    private static void AppendArgument(ref DefaultInterpolatedStringHandler handler, ErrorArgument argument)
    {
        switch (argument.Kind)
        {
            case ErrorArgumentKind.Term:
                handler.AppendFormatted((string?)argument.Value);
                break;
            case ErrorArgumentKind.Sensitive:
                handler.AppendLiteral(ErrorArgument.RedactedText);
                break;
            default:
                AppendData(ref handler, argument.Value);
                break;
        }
    }

    // A value from a caller, shown only because the template asked for it: kept to one short line, so it cannot carry a line
    // break into a log or a header, or a page of text into a message.
    private static void AppendData(ref DefaultInterpolatedStringHandler handler, object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var length = Math.Min(text.Length, MaximumDisplayedDataLength);

        for (var i = 0; i < length; i++)
        {
            handler.AppendFormatted(char.IsControl(text[i]) ? ' ' : text[i]);
        }

        if (text.Length > length)
        {
            handler.AppendLiteral("…");
        }
    }

    private void Validate(ReadOnlySpan<ErrorArgument> arguments, Span<int> map)
    {
        map.Fill(-1);

        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];

            if (argument.Name is null)
            {
                throw Invalid($"argument {i} is default; make arguments with ErrorArgument.Term, Data or Sensitive.");
            }

            for (var j = 0; j < i; j++)
            {
                if (string.Equals(arguments[j].Name, argument.Name, StringComparison.Ordinal))
                {
                    throw Invalid($"the argument '{argument.Name}' is given twice.");
                }
            }

            var placeholder = Array.IndexOf(_names, argument.Name);

            if (placeholder < 0)
            {
                if (argument.Kind == ErrorArgumentKind.Term)
                {
                    throw Invalid($"the term '{argument.Name}' is not a placeholder of \"{Format}\".");
                }

                continue;
            }

            if (argument.Kind == ErrorArgumentKind.Data && !ShowData)
            {
                throw Invalid($"the data '{argument.Name}' fills a placeholder, but this template does not show data. Make it a term, or declare the template with showData.");
            }

            map[placeholder] = i;
        }

        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] < 0)
            {
                throw Invalid($"no argument for the placeholder '{_names[i]}' of \"{Format}\".");
            }
        }
    }

    private ArgumentException Invalid(string reason) =>
        new($"The arguments for the error template '{Code}' are not valid: {reason}");

    private static (string[] Literals, string[] Placeholders) Parse(string format)
    {
        var literals = new List<string>();
        var placeholders = new List<string>();
        var literal = new StringBuilder();

        for (var i = 0; i < format.Length; i++)
        {
            var current = format[i];

            if (current == '{')
            {
                if (i + 1 < format.Length && format[i + 1] == '{')
                {
                    literal.Append('{');
                    i++;

                    continue;
                }

                var end = format.IndexOf('}', i + 1);

                if (end < 0)
                {
                    throw InvalidFormat(format, "a '{' is never closed; write '{{' for a literal brace");
                }

                var name = format.Substring(i + 1, end - i - 1);

                if (!IsName(name))
                {
                    throw InvalidFormat(format, $"'{{{name}}}' is not a placeholder name; use a plain name such as {{Entity}} (no positions, formats or alignment)");
                }

                if (placeholders.Contains(name))
                {
                    throw InvalidFormat(format, $"the placeholder '{{{name}}}' appears twice");
                }

                if (placeholders.Count == MaximumPlaceholders)
                {
                    throw InvalidFormat(format, $"it has more than {MaximumPlaceholders} placeholders");
                }

                literals.Add(literal.ToString());
                literal.Clear();
                placeholders.Add(name);
                i = end;
            }
            else if (current == '}')
            {
                if (i + 1 < format.Length && format[i + 1] == '}')
                {
                    literal.Append('}');
                    i++;

                    continue;
                }

                throw InvalidFormat(format, "a '}' has no matching '{'; write '}}' for a literal brace");
            }
            else
            {
                literal.Append(current);
            }
        }

        literals.Add(literal.ToString());

        return ([.. literals], [.. placeholders]);
    }

    private static bool IsName(string name)
    {
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static ArgumentException InvalidFormat(string format, string reason) =>
        new($"\"{format}\" is not a valid error template: {reason}.", nameof(format));

    private readonly ref struct TermState(string[] literals, ReadOnlySpan<ErrorArgument> arguments, ReadOnlySpan<int> map)
    {
        public readonly string[] Literals = literals;
        public readonly ReadOnlySpan<ErrorArgument> Arguments = arguments;
        public readonly ReadOnlySpan<int> Map = map;
    }
}
