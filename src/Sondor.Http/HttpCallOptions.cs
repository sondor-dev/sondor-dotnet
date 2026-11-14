namespace Sondor.Http;

/// <summary>Settings of one call, which take the place of the client's.</summary>
public sealed class HttpCallOptions
{
    /// <summary>The longest this call may take. Replaces <see cref="HttpResultOptions.Timeout"/>. Must be positive or infinite.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The largest successful body this call accepts, in bytes. Replaces <see cref="HttpResultOptions.MaxResponseBytes"/>.</summary>
    public long? MaxResponseBytes { get; init; }
}
