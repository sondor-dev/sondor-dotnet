namespace Sondor.Results.Localization;

/// <summary>The text of an <see cref="Error"/> for a person, in one culture.</summary>
/// <param name="Title">A short phrase for the kind of failure, such as <c>Not found</c>. Never empty.</param>
/// <param name="Detail">The message, such as <c>No Order was found for the given Id.</c> Never empty.</param>
/// <param name="IsFallback">
/// True when the culture is not the source culture and some of the text is the English fallback because no translation was
/// found (or the one found could not be used). The text is still correct to show; this says it is not in the reader's language, so
/// a caller can count it, log it, or avoid claiming a language it did not use.
/// </param>
public readonly record struct LocalizedError(string Title, string Detail, bool IsFallback);
