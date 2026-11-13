namespace Sondor.Observability.Internal;

/// <summary>What the process around us offers: its variables and its secret folders. A seam, so tests need not touch the real ones.</summary>
internal sealed class Ambient(Func<string, string?> variable, IReadOnlyList<string> secretDirectories)
{
    public static Ambient Process { get; } = new(
        Environment.GetEnvironmentVariable,
        ["/run/secrets", @"C:\ProgramData\Docker\secrets"]);

    public string? Variable(string name) => variable(name) is { Length: > 0 } value ? value : null;

    public IReadOnlyList<string> SecretDirectories => secretDirectories;
}
