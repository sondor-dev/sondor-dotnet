namespace Sondor.Results;

/// <summary>
/// What an <see cref="ErrorArgument"/> is, which decides whether it may ever be shown to a person.
/// </summary>
public enum ErrorArgumentKind
{
    /// <summary>
    /// Text the developer chose: an entity name, a property name, an action. Safe to show, and the natural thing to translate.
    /// </summary>
    Term,

    /// <summary>
    /// A value that may have come from a caller or from stored data, such as an id or a search text. Reflecting it into a
    /// response is an injection and information-leak risk, so it is never put in an error's description unless the template
    /// asks for it, and the layers above this package do not show it by default.
    /// </summary>
    Data,

    /// <summary>
    /// A value that must never leave the process: personal data, a secret, a token. Only the argument's name is recorded; the
    /// value is dropped when the argument is made, so it cannot be logged, serialised or shown by mistake later.
    /// </summary>
    Sensitive
}
