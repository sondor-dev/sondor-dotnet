using Names = Sondor.Results.ErrorArgumentNames;

namespace Sondor.Results;

// The common errors, one call each. Each is the matching ErrorTemplates entry with its arguments filled in; to give one a code of
// your own, use ErrorTemplates.<Kind>.WithCode(...).Create(...).
public readonly partial record struct Error
{
    /// <summary>Something that was asked for does not exist. Code <c>Resource.NotFound</c>.</summary>
    /// <param name="entity">The kind of thing, such as <c>Order</c>.</param>
    /// <param name="property">What it was looked up by, such as <c>Id</c>.</param>
    /// <param name="value">The value looked up. Kept as data on the error for logging; never put in the description.</param>
    public static Error NotFound(string entity, string property, object? value = null) =>
        value is null
            ? ErrorTemplates.NotFound.Create(ErrorArgument.Term(Names.Entity, entity), ErrorArgument.Term(Names.Property, property))
            : ErrorTemplates.NotFound.Create(ErrorArgument.Term(Names.Entity, entity), ErrorArgument.Term(Names.Property, property), ErrorArgument.Data(Names.Value, value));

    /// <summary>The request clashes with what already exists, for example a duplicate. Code <c>Resource.Conflict</c>.</summary>
    /// <param name="entity">The kind of thing, such as <c>Customer</c>.</param>
    /// <param name="property">What clashes, such as <c>Email</c>.</param>
    /// <param name="value">The value that clashes. Kept as data on the error for logging; never put in the description.</param>
    public static Error Conflict(string entity, string property, object? value = null) =>
        value is null
            ? ErrorTemplates.Conflict.Create(ErrorArgument.Term(Names.Entity, entity), ErrorArgument.Term(Names.Property, property))
            : ErrorTemplates.Conflict.Create(ErrorArgument.Term(Names.Entity, entity), ErrorArgument.Term(Names.Property, property), ErrorArgument.Data(Names.Value, value));

    /// <summary>A value is not valid. Code <c>Resource.Invalid</c>, type <see cref="ErrorType.Validation"/>.</summary>
    /// <param name="entity">The kind of thing, such as <c>Invoice</c>.</param>
    /// <param name="property">The property that is not valid, such as <c>DueDate</c>.</param>
    public static Error Invalid(string entity, string property) =>
        ErrorTemplates.Invalid.Create(ErrorArgument.Term(Names.Entity, entity), ErrorArgument.Term(Names.Property, property));

    /// <summary>The caller is not signed in. Code <c>Request.Unauthorized</c>.</summary>
    /// <param name="action">What they tried, written to follow "to", such as <c>view invoices</c>.</param>
    public static Error Unauthorized(string action) =>
        ErrorTemplates.Unauthorized.Create(ErrorArgument.Term(Names.Action, action));

    /// <summary>The caller is signed in but not allowed. Code <c>Request.Forbidden</c>.</summary>
    /// <param name="action">What they tried, written to follow "to", such as <c>cancel the order</c>.</param>
    public static Error Forbidden(string action) =>
        ErrorTemplates.Forbidden.Create(ErrorArgument.Term(Names.Action, action));

    /// <summary>The caller has made too many requests. Code <c>Request.RateLimited</c>.</summary>
    /// <param name="action">What they were doing, written to follow "to", such as <c>search customers</c>.</param>
    public static Error RateLimited(string action) =>
        ErrorTemplates.RateLimited.Create(ErrorArgument.Term(Names.Action, action));

    /// <summary>The feature is switched off. Code <c>Feature.Disabled</c>.</summary>
    /// <param name="feature">The feature, such as <c>Invoice export</c>.</param>
    public static Error FeatureDisabled(string feature) =>
        ErrorTemplates.FeatureDisabled.Create(ErrorArgument.Term(Names.Feature, feature));

    /// <summary>
    /// The operation failed in a way the caller did not cause and cannot fix. Code <c>Error.Internal</c>, type
    /// <see cref="ErrorType.InternalServerError"/>.
    /// </summary>
    /// <param name="action">What was being attempted, written to follow "to", such as <c>save the order</c>.</param>
    public static Error Internal(string action) =>
        ErrorTemplates.Internal.Create(ErrorArgument.Term(Names.Action, action));

    /// <summary>The operation failed in an expected way that fits no other kind. Code <c>Error.Failed</c>.</summary>
    /// <param name="action">What was being attempted, written to follow "to", such as <c>import the file</c>.</param>
    public static Error Failed(string action) =>
        ErrorTemplates.Failed.Create(ErrorArgument.Term(Names.Action, action));
}
