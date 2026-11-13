using System.Globalization;

namespace Sondor.Results.Localization;

/// <summary>
/// Turns an <see cref="Error"/> into text for a person, in a culture: the title and the message, from the application's own
/// translations, falling back to the English text the error already carries.
/// </summary>
/// <remarks>
/// <para>
/// It looks an error up by its <see cref="Error.Code"/>, then by the general template the error was made from
/// (<see cref="ErrorArguments.Template"/>), and uses <see cref="Error.Description"/> when nothing is found, so a message is always
/// returned. Only the human text is translated, never the code. An argument of an error is shown only if it is a
/// <see cref="ErrorArgumentKind.Term"/> (a name the developer chose), or data the application has listed in
/// <see cref="ErrorLocalizationOptions.ShowDataFor"/>. An <see cref="ErrorArgumentKind.Sensitive"/> argument is only ever
/// <c>[redacted]</c>.
/// </para>
/// <para>An instance is thread-safe and meant to be shared.</para>
/// </remarks>
public interface IErrorLocalizer
{
    /// <summary>The text of an error in <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    /// <param name="failure">A failure. <see cref="Error.None"/>, the error of a success, has nothing to translate.</param>
    /// <returns>The title and the message. Neither is ever empty.</returns>
    /// <exception cref="ArgumentException"><paramref name="failure"/> is <see cref="Error.None"/>.</exception>
    LocalizedError Localize(Error failure);

    /// <summary>
    /// The text of an error in a given culture, for a notification sent in the recipient's language or a background job that has no
    /// request. <see cref="CultureInfo.CurrentUICulture"/> is set to <paramref name="culture"/> for the duration of the call and restored
    /// afterwards, because that is what a localizer reads.
    /// </summary>
    /// <param name="failure">A failure. <see cref="Error.None"/>, the error of a success, has nothing to translate.</param>
    /// <param name="culture">The culture of the reader.</param>
    /// <returns>The title and the message. Neither is ever empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="failure"/> is <see cref="Error.None"/>.</exception>
    LocalizedError Localize(Error failure, CultureInfo culture);
}
