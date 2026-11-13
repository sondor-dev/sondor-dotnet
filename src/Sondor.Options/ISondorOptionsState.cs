namespace Sondor.Options;

/// <summary>Tells whether the configuration section of <typeparamref name="TOptions"/> is there.</summary>
/// <typeparam name="TOptions">The options type.</typeparam>
/// <remarks>
/// Read from the live configuration on every call, so it follows a reload (for example an Azure App Configuration refresh).
/// A section counts as configured when it has at least one key or value: an empty <c>"Feature": {}</c> does not.
/// </remarks>
public interface ISondorOptionsState<TOptions>
    where TOptions : class
{
    /// <summary>Gets a value indicating whether the section has at least one key or value.</summary>
    bool IsConfigured { get; }
}
