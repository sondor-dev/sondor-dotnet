namespace Sondor.Observability.Internal;

/// <summary>Secrets that arrive as files, the way Docker and Kubernetes hand them over.</summary>
internal static class Secrets
{
    /// <summary>The contents of the file when <paramref name="value"/> is the path of a file inside one of the secret folders, otherwise the value itself.</summary>
    public static string Resolve(string value, IReadOnlyList<string> directories)
    {
        if (!Path.IsPathFullyQualified(value))
        {
            return value;
        }

        var path = Path.GetFullPath(value);

        foreach (var directory in directories)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (path.StartsWith(root, StringComparison.Ordinal) && ReadFile(path) is { } content)
            {
                return content;
            }
        }

        return value;
    }

    /// <summary>The trimmed contents of the file, or null when it cannot be read.</summary>
    public static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
