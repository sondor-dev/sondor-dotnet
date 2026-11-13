namespace Sondor.Results;

/// <summary>The placeholder names the built-in <see cref="ErrorTemplates"/> use, so code that reads them does not repeat the spelling.</summary>
public static class ErrorArgumentNames
{
    /// <summary>The kind of thing, such as <c>Order</c>.</summary>
    public const string Entity = "Entity";

    /// <summary>The property of the entity that was looked up or is not valid, such as <c>Id</c>.</summary>
    public const string Property = "Property";

    /// <summary>What was being attempted, written to follow "to", such as <c>cancel the order</c>.</summary>
    public const string Action = "Action";

    /// <summary>The feature that is switched off.</summary>
    public const string Feature = "Feature";

    /// <summary>The value that was looked up or rejected. Always <see cref="ErrorArgumentKind.Data"/>.</summary>
    public const string Value = "Value";
}
