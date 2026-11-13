namespace Sondor.Options;

/// <summary>What happens when configuration reloads (an Azure App Configuration refresh, a changed file) into invalid options.</summary>
/// <remarks>Starting the host is always strict: the options must be valid, or the host does not start.</remarks>
public enum InvalidReloadBehavior
{
    /// <summary>
    /// The reload is rejected: the options last found valid stay in use, and a warning is logged with the reason. One bad
    /// change in a store such as Azure App Configuration cannot take a running service down. A later valid change applies.
    /// </summary>
    KeepLastValid,

    /// <summary>
    /// The framework's behaviour: reading <c>IOptionsMonitor&lt;T&gt;.CurrentValue</c> throws until the configuration is
    /// valid again, and the failure also surfaces from the code that triggered the reload.
    /// </summary>
    Fail,
}
