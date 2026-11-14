namespace Sondor.Http.Internal;

internal static class MediaTypes
{
    /// <summary>JSON by its media type: <c>application/json</c> or any structured syntax suffix, such as <c>application/problem+json</c>.</summary>
    public static bool IsJson(string? mediaType) =>
        mediaType is not null &&
        (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
         mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
}
