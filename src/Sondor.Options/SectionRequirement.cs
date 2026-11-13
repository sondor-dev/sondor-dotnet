namespace Sondor.Options;

/// <summary>Whether a configuration section has to be there.</summary>
public enum SectionRequirement
{
    /// <summary>The section must exist and be valid. Without it the host fails to start, naming the section.</summary>
    Required,

    /// <summary>
    /// The section may be missing, which means "not configured": nothing is validated and nothing fails. A section that is
    /// there must still be valid.
    /// </summary>
    Optional,
}
