# Sondor.Results.Localization

Translated text for a [`Sondor.Results`](../Sondor.Results) `Error`: a **title** and a **message** in the reader's language,
from **your own resources**, with the English text the error already carries as the fallback. It does not depend on a web
framework, so the same text serves an HTTP API, a Blazor or MVC view, a notification or a command line tool.

```bash
dotnet add package Sondor.Results.Localization
```

```csharp
builder.Services
    .AddLocalization()                                              // resx files; or register your own IStringLocalizerFactory
    .AddErrorLocalization(options => options.UseResource<ErrorMessages>());

// anywhere an error needs showing:
LocalizedError text = localizer.Localize(Error.NotFound("Order", "Id"));    // in CultureInfo.CurrentUICulture
// text.Title:  "Introuvable"
// text.Detail: "Aucune commande n'a été trouvée pour l'identifiant."
```

## What it does, and does not do

- **It looks an error up**, it does not translate for you. Translations are resources your application owns (`.resx`, JSON, a
  database): anything behind `IStringLocalizer`. No language ships in the package except English, which is built in.
- **Only the human text is translated**, never the error `Code`. Clients depend on the code.
- **A message is always returned.** Layer by layer: the message for the error's code, then for the general template it was made
  from, then the English text. `LocalizedError.IsFallback` says when English was used for a reader who is not an English reader.
- **Request data is never shown unless you say so** (see [What is shown](#what-is-shown)).
- **It follows the current UI culture.** ASP.NET Core's request localization sets it from `Accept-Language`; a worker sets it
  itself or uses `Localize(error, culture)`.
- **English costs nothing.** For the source culture (`en` and its regional variants) the text is returned as it is, with no lookup.

If your application has a handful of messages and no `Sondor.Results` errors made from templates, you can translate them with
`IStringLocalizer` directly. This package is for an application where every failure is an `Error` and you want the lookup,
the layering and the data rules done once.

## How an error is found

An error made with `Error.NotFound("Order", "Id")` carries a code (`Resource.NotFound`), the template it came from, and named
arguments (`Entity = Order`, `Property = Id`). A translated message is a **whole sentence** with the same `{Placeholder}` names the
English one uses, so a translator can reorder words, add articles, and leave a placeholder out.

| What | Key | Example |
|---|---|---|
| The message of one code | `{Code}.Detail` | `Order.NotFound.Detail` |
| The message of the general template | `{Template}.Detail` | `Resource.NotFound.Detail` |
| The title of one code, then the template | `{Code}.Title`, `{Template}.Title` | `Resource.NotFound.Title` |
| The title of a kind of failure | `Type.{ErrorType}.Title` | `Type.NotFound.Title` |
| A word a sentence fills in | `{Name}.{Text}` | `Entity.Order`, `Property.Id`, `Action.view invoices` |
| The same word in the form one sentence needs (optional) | `{Template}.{Name}.{Text}` | `Resource.NotFound.Entity.Order` |

The first key found wins, most specific first. A word with no entry stays as written (`Order`), which is right for a name that
is the same in every language. Put every key behind a prefix with `options.KeyPrefix = "Errors."` if the file is shared with
other text.

### The built-in templates

These are what `Error.NotFound(...)` and the other factories build, so these are the keys to translate first. The English text
is also what is returned until you do.

| Key (`.Detail`) | English | Placeholders |
|---|---|---|
| `Resource.NotFound` | No {Entity} was found for the given {Property}. | `Entity`, `Property` |
| `Resource.Conflict` | A {Entity} with that {Property} already exists. | `Entity`, `Property` |
| `Resource.Invalid` | The {Property} of the {Entity} is not valid. | `Entity`, `Property` |
| `Request.Unauthorized` | You need to sign in to {Action}. | `Action` |
| `Request.Forbidden` | You do not have permission to {Action}. | `Action` |
| `Request.RateLimited` | Too many requests were made to {Action}. Try again later. | `Action` |
| `Feature.Disabled` | {Feature} is not available. | `Feature` |
| `Error.Internal` | An unexpected error occurred while trying to {Action}. | `Action` |
| `Error.Failed` | Could not {Action}. | `Action` |

| Key (`Type.….Title`) | English |
|---|---|
| `Validation` | Validation failed |
| `NotFound` | Not found |
| `Conflict` | Conflict |
| `Unauthorized` | Unauthorized |
| `Forbidden` | Forbidden |
| `RateLimited` | Too many requests |
| `FeatureDisabled` | Not available |
| `InternalServerError` | Unexpected error |
| `Failure` | Request failed |

An error you declare yourself (`ErrorTemplates.NotFound.WithCode("Order.NotFound")`, or a template of your own) is looked up by
its own code first, then by the general template it came from, so you only write a specific message where the general one is not
enough.

## Resource files

### `.resx`

```
Resources/ErrorMessages.resx        (English; optional)
Resources/ErrorMessages.fr.resx
```

```xml
<data name="Resource.NotFound.Detail"><value>Aucune {Entity} n'a été trouvée pour {Property}.</value></data>
<data name="Entity.Order"><value>commande</value></data>
<data name="Property.Id"><value>l'identifiant</value></data>
<data name="Type.NotFound.Title"><value>Introuvable</value></data>
```

`ErrorMessages` is an empty class next to the files, the marker `IStringLocalizer<T>` uses. Add it with
`options.UseResource<ErrorMessages>()` after `AddLocalization()`.

### JSON, a database, anything else

Register your own `IStringLocalizerFactory` instead of calling `AddLocalization()`. The package asks it for a localizer and reads
`localizer[key]`; a result whose `ResourceNotFound` is true (or whose value is blank) counts as missing. A minimal JSON store:

```csharp
sealed class JsonLocalizerFactory(string folder) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => Create(resourceSource.Name, string.Empty);

    public IStringLocalizer Create(string baseName, string location) => new JsonLocalizer(folder, baseName);
}

sealed class JsonLocalizer(string folder, string name) : IStringLocalizer
{
    public LocalizedString this[string key]
    {
        get
        {
            // CurrentUICulture is "fr-CA"; try "fr-CA", then "fr".
            for (var culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent)
            {
                var path = Path.Combine(folder, $"{name}.{culture.Name}.json");   // cache the parsed file in real code
                if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) is { } map
                    && map.TryGetValue(key, out var value))
                {
                    return new LocalizedString(key, value);
                }
            }

            return new LocalizedString(key, key, resourceNotFound: true);
        }
    }

    public LocalizedString this[string key, params object[] arguments] => this[key];
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
```

The package only references `Microsoft.Extensions.Localization.Abstractions` (the interfaces), so choosing resx, JSON or a database
is yours and nothing you do not use is pulled in. An application that uses resx calls `AddLocalization()` itself, as a web
application almost always already does; if a resource was added and no factory is registered, the host fails to start and says so.

### Several resource files

One file does not have to hold every message. Add as many resources as you like; they are searched in the order added and the
first one holding a key wins. Limit a resource to the **areas** of the codes it holds (the part of a code before the dot) so it is
only asked about those:

```csharp
builder.Services.AddLocalization().AddErrorLocalization(options => options
    .UseResource<OrderErrors>("Order", "Cart")      // Order.* and Cart.* codes
    .UseResource<BillingErrors>("Invoice")          // Invoice.* codes
    .UseResource<SharedErrors>());                  // no areas: the general templates, the titles and the vocabulary
```

A resource without areas is asked about every key. The vocabulary (`Entity.Order`) has no area, so it is only read from a resource
without areas, normally the shared one.

## What is shown

Each argument of an error has a kind, set where the error is made:

| Kind | Shown in a translated message? |
|---|---|
| `Term`: a name the developer chose (an entity, a property, an action) | Yes, and translated through the vocabulary |
| `Data`: a value that came from a caller (an id, an email) | **No**, unless the code is listed in `ShowDataFor`. A sentence that names it without that is reported and skipped. |
| `Sensitive`: personal data or a secret | Only ever `[redacted]` |

```csharp
options.ShowDataFor.Add("Order.NotFound");   // "Commande {Value} introuvable." may now show the id
```

A value that is shown is put on one line and cut at 64 characters, with the same rules as the English text. Showing request data
in a response is how a message becomes an injection or a leak, so the decision is written down once, in your setup.

## Missing translations are never silent

A fallback to English is right (the reader still gets a message), but you need to know it happened. Three signals, none of which
changes what the reader sees:

- **`LocalizedError.IsFallback`** is true when the culture is not the source culture and any of the text is the English fallback.
- **A log entry** for each culture and key, once, at `MissingTranslationLevel` (`Warning` by default; `Debug` or `None` for an
  application that is deliberately translated in part). It names the key to add:
  `No translation for the key 'Resource.NotFound.Detail' in the culture 'de'; the English text is used.`
- **A counter**, `sondor.results.localization.missing_translations`, on the meter `Sondor.Results.Localization`
  (tags: `culture` and `area`, never a value). It costs nothing until something listens; add the meter to OpenTelemetry with
  `.AddMeter("Sondor.Results.Localization")` to chart which cultures are behind.

A translation that exists but cannot be used (it does not parse, it names a placeholder the error does not have, it names data
you have not allowed) is reported once as a warning with the reason, and the next layer is used. A mistake in a translation file
never breaks a request.

A word of the vocabulary with no entry (the entity `Order` stays `Order`) is often deliberate, so it is only logged at `Debug`
and does not set `IsFallback`.

### Check your translations in a test

```csharp
foreach (var error in new[] { Error.NotFound("Order", "Id"), Error.Unauthorized("view invoices") /* ...the errors you produce */ })
{
    Assert.That(localizer.Localize(error, new CultureInfo("fr")).IsFallback, Is.False, error.Code);
}
```

## Settings

| Option | Default | Meaning |
|---|---|---|
| `UseResource<T>(areas...)`, `UseResource(baseName, location, areas...)` | none | A resource to search. With none added, nothing is looked up and every error keeps its English text. |
| `KeyPrefix` | empty | Put in front of every key. |
| `SourceCulture` | `en` | The language the built-in text is written in. It and its regional variants (`en-GB`) get the English text with no lookup. `null` looks English up like any language, to reword it in your resources. |
| `MissingTranslationLevel` | `Warning` | How loudly a missing translation is logged, once for each culture and key. |
| `ShowDataFor` | empty | Codes (or general template codes) whose translated message may show a `Data` argument. |

The options are validated when the host starts.

### In another culture, without a request

```csharp
LocalizedError text = localizer.Localize(error, new CultureInfo("de"));   // a notification in the recipient's language
```

`CultureInfo.CurrentUICulture` is set for the duration of the call and put back afterwards, because that is what an
`IStringLocalizer` reads.

## Not included

- **Translations.** English is built in; every other language is a resource you supply.
- Plurals, grammatical gender, ICU message format, and number or date formatting. A placeholder is plain text.
- Choosing the culture (that is ASP.NET Core's request localization, or the caller's `CurrentUICulture`).
- A claim about the language of a response. `IStringLocalizer` does not say which language a string came from, so use
  `IsFallback` rather than assuming the requested one.
- A tool that lists the keys an application is missing. Codes are values your application produces, so the package cannot know
  them all; the signals above and a test over your errors cover it.
