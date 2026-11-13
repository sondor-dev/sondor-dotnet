using Microsoft.Extensions.Configuration;

namespace Sondor.Options;

internal sealed class SectionState<TOptions>(IConfiguration configuration, string path) : ISondorOptionsState<TOptions>
    where TOptions : class
{
    public bool IsConfigured => configuration.GetSection(path).Exists();
}
