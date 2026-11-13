namespace Sondor.Options;

internal static class SectionName
{
    /// <summary>The path given, or the name of the options type.</summary>
    internal static string For<TOptions>(string? section) =>
        string.IsNullOrWhiteSpace(section) ? typeof(TOptions).Name : section;
}
